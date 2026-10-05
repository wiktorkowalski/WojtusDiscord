#!/usr/bin/env python3
"""Retrieval eval for meme search (#370): owner-style queries against the real search SQL.

The SQL in rank_sql is MemeSearchService.SearchAsync, copied verbatim apart from inlined
parameters and the four columns the service projects for its search log (#384), which take no
part in the ranking. Keep the two in step: a change to the ranking there must be repeated here.
STOP_WORDS is MemeSearchService.RankStopWords.

A writer set is scored through a temporary view named meme_annotations that holds only that set's
rows. pg_temp resolves before public, so the query text stays the production query (best
annotation per attachment) and nothing is written.

    python3 retrieval_eval.py --inputs <dir> --guild <id> [--db meme_eval] [--out results.json]

<dir> holds blindq*.json ([{id, queries: [{q, type}]}]) and optionally labels/<attachmentId>.json
({data: {own: "query, query"}}), as exported from the calibration artifact.

Tokenizer variants:
  prod    what SearchAsync does since #380: split on whitespace, delete every non letter/digit;
          stop list in the ts_rank query only, when another token remains
  nostop  search before #380: every token in both queries
  split   prod, but every non letter/digit is a separator ("korwin-mikke" -> korwin, mikke)
  filter  stop list in the WHERE filter too (measured on #370: it loses rows; kept as the control)
The raw query that feeds word_similarity is never changed.

Every row is compared with the baseline: the prod tokenizer at the production trigram weight.

--glued-probes adds synthetic queries for the hyphen question: every weight-A term in the corpus
with punctuation inside a word ("spider-man", "assassin's creed") is searched as written, and
the attachment that carries it is the target. They are reported apart from the owner-style
queries, under type "glued".
"""
import argparse
import collections
import json
import pathlib
import subprocess
import sys

INDEXED = 1  # MemeIndexStatus.Indexed
PROD_TRIGRAM_WEIGHT = 0.5  # MemeSearchService.TrigramWeight
TRIGRAM_THRESHOLD = 0.4  # MemeSearchService.TrigramThreshold
TOP_K = 5  # MemeSearchService.DefaultLimit
RANK_DEPTH = 100  # SearchAsync takes a limit; the eval needs ranks past 5 for MRR

STOP_WORDS = frozenset(
    "w we z ze na do od o u i a po za to ten ta te tym jak co sie się że czy dla "
    "the an of in on at is and or for with".split())


def unique(tokens):
    return list(dict.fromkeys(t for t in tokens if t))


def tokenize_prod(query):
    return unique("".join(ch for ch in word if ch.isalnum()) for word in query.lower().split())


def tokenize_split(query):
    return unique("".join(ch if ch.isalnum() else " " for ch in query.lower()).split())


def drop_stop_words(tokens):
    return [t for t in tokens if t not in STOP_WORDS] or tokens


def rank_only(tokenize):
    """Stop list in the ts_rank query only, as SearchAsync does."""
    def tokens_for(query):
        tokens = tokenize(query)
        return drop_stop_words(tokens), tokens
    return tokens_for


# name -> query -> (tokens for ts_rank, tokens for the WHERE filter)
TOKENIZERS = {
    "prod": rank_only(tokenize_prod),
    "nostop": lambda q: (tokenize_prod(q),) * 2,
    "split": rank_only(tokenize_split),
    "filter": lambda q: (drop_stop_words(tokenize_prod(q)),) * 2,
}


def lit(text):
    return "'" + text.replace("'", "''") + "'"


class Db:
    def __init__(self, container, database):
        self.command = ["docker", "exec", "-i", container, "psql", "-U", "postgres", "-d", database,
                        "-At", "-v", "ON_ERROR_STOP=1"]

    def run(self, sql):
        result = subprocess.run(self.command, input=sql, capture_output=True, text=True)
        if result.returncode:
            sys.exit(result.stderr)
        return result.stdout


def rank_sql(index, guild, target, query, rank_tokens, filter_tokens, trigram_weight):
    rank_query = lit(" | ".join(rank_tokens))
    filter_query = lit(" | ".join(filter_tokens))
    raw = lit(query)
    return f"""
SELECT {index}, COALESCE((SELECT r FROM (
  SELECT attachment_discord_id, row_number() OVER (ORDER BY score DESC, message_created_at_utc DESC, attachment_discord_id) AS r
  FROM (
    SELECT DISTINCT ON (m.attachment_discord_id)
           m.attachment_discord_id,
           msg.created_at_utc AS message_created_at_utc,
           (ts_rank(a.search_vector, to_tsquery('simple', public.f_unaccent({rank_query})))
            + {trigram_weight} * word_similarity(public.f_unaccent({raw}), a.search_text))::float8 AS score
    FROM meme_annotations AS a
    JOIN meme_index AS m ON m.id = a.meme_index_id
    JOIN messages AS msg ON msg.id = m.message_id
    WHERE m.guild_discord_id = {guild}
      AND m.status = {INDEXED}
      AND NOT msg.is_deleted
      AND (a.search_vector @@ to_tsquery('simple', public.f_unaccent({filter_query}))
           OR word_similarity(public.f_unaccent({raw}), a.search_text) >= {TRIGRAM_THRESHOLD})
    ORDER BY m.attachment_discord_id, score DESC, a.indexed_at_utc DESC, a.model_id, a.prompt_version
  ) AS best
  ORDER BY score DESC, message_created_at_utc DESC, attachment_discord_id
  LIMIT {RANK_DEPTH}
) AS ranked WHERE attachment_discord_id = {target}), 0);"""


def ranks(db, guild, queries, writers, tokenizer, trigram_weight):
    """Rank of the target per query (0 = not returned), with only `writers` visible."""
    keep = ",".join(lit(m) for m in writers)
    statements = ["CREATE TEMP VIEW meme_annotations AS "
                  f"SELECT * FROM public.meme_annotations WHERE model_id IN ({keep});"]
    for index, (target, text, _) in enumerate(queries):
        rank_tokens, filter_tokens = tokenizer(text)
        if rank_tokens:
            statements.append(rank_sql(index, guild, target, text, rank_tokens, filter_tokens, trigram_weight))

    found = {}
    for line in db.run("\n".join(statements)).split("\n"):
        if "|" in line:
            index, rank = map(int, line.split("|"))
            found[index] = rank
    return [found.get(i, 0) for i in range(len(queries))]


def rate(rank_list, hit):
    return sum(hit(r) for r in rank_list) / len(rank_list)


def is_top1(rank):
    return rank == 1


def in_top_k(rank):
    return 1 <= rank <= TOP_K


def summarize(queries, rank_list):
    by_type = collections.defaultdict(list)
    for (_, _, kind), rank in zip(queries, rank_list):
        by_type[kind].append(rank)
    by_type = dict(sorted(by_type.items()))
    return {
        "n": len(rank_list),
        "top1": rate(rank_list, is_top1),
        "recall5": rate(rank_list, in_top_k),
        "mrr": sum(1 / r for r in rank_list if r) / len(rank_list),
        "not_returned": sum(r == 0 for r in rank_list),
        "recall5_by_type": {k: round(rate(v, in_top_k), 3) for k, v in by_type.items()},
        "top1_by_type": {k: round(rate(v, is_top1), 3) for k, v in by_type.items()},
    }


def paired(base, other):
    """Queries whose rank got better / worse against the baseline (0 = not returned = worst)."""
    key = lambda r: r or RANK_DEPTH + 1
    pairs = list(zip(base, other))
    return {
        "better": sum(key(o) < key(b) for b, o in pairs),
        "worse": sum(key(o) > key(b) for b, o in pairs),
        "top1_gained": sum(o == 1 and b != 1 for b, o in pairs),
        "top1_lost": sum(b == 1 and o != 1 for b, o in pairs),
    }


def sweep(db, guild, queries, writers, weights):
    """One result row per (trigram weight, tokenizer), each compared with production search."""
    label = " + ".join(m.split("/")[-1] for m in writers)
    baseline = ranks(db, guild, queries, writers, TOKENIZERS["prod"], PROD_TRIGRAM_WEIGHT)
    rows = []
    for weight in weights:
        for name, tokenizer in TOKENIZERS.items():
            is_baseline = name == "prod" and weight == PROD_TRIGRAM_WEIGHT
            rank_list = baseline if is_baseline else ranks(db, guild, queries, writers, tokenizer, weight)
            row = {"writers": writers, "tokenizer": name, "trigram_weight": weight,
                   **summarize(queries, rank_list), "ranks": rank_list}
            if is_baseline:
                versus = "  (baseline)"
            else:
                delta = row["vs_baseline"] = paired(baseline, rank_list)
                versus = (f"  better={delta['better']} worse={delta['worse']} "
                          f"top1 +{delta['top1_gained']}/-{delta['top1_lost']}")
            rows.append(row)
            print(f"{label:44} tok={name:6} k={weight:<4} top1={row['top1']:.1%} r@5={row['recall5']:.1%} "
                  f"MRR={row['mrr']:.3f} miss={row['not_returned']:3}{versus}")
    return rows


def load_queries(inputs):
    queries = []
    for path in sorted(inputs.glob("blindq*.json")):
        for row in json.loads(path.read_text()):
            queries += [(int(row["id"]), q["q"], q["type"]) for q in row["queries"]]
    for path in sorted(inputs.glob("labels/*.json")):
        doc = json.loads(path.read_text())
        own = doc.get("data", doc).get("own") or ""
        queries += [(int(path.stem), q.strip(), "user") for q in own.split(",") if q.strip()]
    return queries


def glued_probes(db, guild):
    """(attachment, term, "glued") for every weight-A term with punctuation inside a word."""
    rows = db.run(f"""
SELECT DISTINCT m.attachment_discord_id || E'\\t' || term
FROM meme_annotations a
JOIN meme_index m ON m.id = a.meme_index_id
CROSS JOIN LATERAL unnest(a.templates || a.people_names || a.tags || ARRAY[COALESCE(a.franchise, '')]) AS term
WHERE m.guild_discord_id = {guild} AND term ~ '[[:alnum:]][-''’.][[:alnum:]]'
ORDER BY 1;""")
    return [(int(target), term, "glued")
            for target, _, term in (line.partition("\t") for line in rows.split("\n")) if term]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--inputs", required=True, type=pathlib.Path)
    parser.add_argument("--guild", required=True, type=int)
    parser.add_argument("--db", default="meme_eval")
    parser.add_argument("--container", default="wojtus-postgres")
    parser.add_argument("--trigram-weights", default=str(PROD_TRIGRAM_WEIGHT), help="comma list")
    parser.add_argument("--glued-probes", action="store_true")
    parser.add_argument("--out", type=pathlib.Path)
    args = parser.parse_args()

    db = Db(args.container, args.db)
    queries = load_queries(args.inputs)
    models = [m for m in db.run(
        "SELECT DISTINCT a.model_id FROM meme_annotations a JOIN meme_index m ON m.id = a.meme_index_id "
        f"WHERE m.guild_discord_id = {args.guild} ORDER BY 1;").split("\n") if m]
    writer_sets = [[m] for m in models] + ([models] if len(models) > 1 else [])
    weights = [float(w) for w in args.trigram_weights.split(",")]

    print(f"{len(queries)} queries, writers: {', '.join(models)}")
    results = [row for writers in writer_sets for row in sweep(db, args.guild, queries, writers, weights)]

    probes, probe_results = [], []
    if args.glued_probes:
        probes = glued_probes(db, args.guild)
        print(f"\n{len(probes)} glued-term probes (synthetic, all writers present)")
        probe_results = sweep(db, args.guild, probes, models, [PROD_TRIGRAM_WEIGHT])

    if args.out:
        as_json = lambda rows: [{"target": str(t), "q": q, "type": k} for t, q, k in rows]
        payload = {"queries": as_json(queries), "results": results,
                   "glued_probes": as_json(probes), "glued_results": probe_results}
        args.out.write_text(json.dumps(payload, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
