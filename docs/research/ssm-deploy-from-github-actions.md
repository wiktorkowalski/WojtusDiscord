# Deploy from a GitHub-hosted runner via AWS SSM — OIDC role scope and the reusable-workflow shape

Research for ticket #330 (wayfinder map #327 "move prod hosting from homelab to AWS EC2").
Question: replace the self-hosted `[self-hosted, homelab]` deploy job (`cd <compose-path> &&
docker compose pull && docker compose up -d`) with `ubuntu-latest` +
`aws-actions/configure-aws-credentials` (OIDC, no long-lived keys) + `aws ssm send-command`
against one EC2 (Ubuntu 24.04 arm64, eu-central-1), wait for the command, fail the job when it
fails. Pin the minimal IAM policy, the trust policy, GHCR auth on the instance, the second
deploy mode in `wiktorkowalski/github-workflows/.github/workflows/dotnet-docker-deploy.yml`, a
rollback recipe, and whether #326 (concurrency group) survives the move.

Why no runner on the instance: the 2026-09-03 I/O storm — a self-hosted runner's self-update
loop wrote 80+ MB/s and stalled prod Postgres for 30 s+ (agent memory
`project_2026_09_03_io_storm`). A t4g.small with a gp3 volume has less headroom than the homelab
VM had. SSM Agent is a single Go daemon that polls; nothing on the host updates itself in a loop.

Every external claim is cited to a primary source (AWS docs, GitHub docs, action READMEs) or to
something verified in this session (marked **[verified]**). Read against
`.github/workflows/build.yml` (the caller) and agent memory `reference_prod_deploy`.

---

## TL;DR

- **Do it.** OIDC → role → `ssm:SendCommand` on `AWS-RunShellScript` is the documented shape;
  every piece below is a stock AWS/GitHub primitive. No new secrets except one role ARN.
- **IAM (§1):** `ssm:SendCommand` on two resources — the public document ARN
  `arn:aws:ssm:eu-central-1::document/AWS-RunShellScript` (no account id for `AWS-*` docs) and
  `arn:aws:ec2:eu-central-1:<acct>:instance/*` under a `ssm:resourceTag/Service = wojtusdiscord`
  condition — plus `ssm:GetCommandInvocation`/`ssm:ListCommandInvocations` on `*`. Trust
  policy: `StringEquals` on `aud = sts.amazonaws.com` and
  `sub = repo:wiktorkowalski/WojtusDiscord:ref:refs/heads/master`. **[verified]** the repo still
  emits the *legacy* `sub` format (`use_immutable_subject: false`), so no `@<id>` suffixes.
- **GHCR (§2):** the package **is already public** — **[verified]** an anonymous token pulled the
  `latest` and `0bcd622` manifests (HTTP 200). Keep it public; no `docker login` on the host, no
  PAT to rotate. The PAT route is documented as the fallback if the package ever goes private.
- **Workflow (§3):** new job `deploy-ssm` in the reusable workflow, gated on
  `inputs.deploy-ssm-instance-id != ''`; the self-hosted `deploy` job is untouched. Role ARN
  arrives as a **secret** (`AWS_DEPLOY_ROLE_ARN`, via the caller's existing `secrets: inherit`),
  instance id + region as inputs. The caller must add `id-token: write` — a called workflow can
  only downgrade the caller's token permissions, never raise them.
- **Do not use `aws ssm wait command-executed`.** It polls 5 s × 20 = 100 s and exits 255 for
  both "Failed" and "still running" — an image pull can exceed 100 s. Use the 15-line
  `get-command-invocation` loop in §3; it prints stdout/stderr into the job log and propagates
  the exit code.
- **Deploy by sha, not by `:latest` (§4).** The build already pushes a bare 7-char sha tag
  (`type=sha,prefix=` → `0bcd622`, **[verified]** on GHCR). Pass `IMAGE_TAG=${GITHUB_SHA::7}`
  into `docker compose up`; rollback = the same command with an older sha. This also removes
  #326's "stale `:latest` wins" failure mode.
- **#326 stays (§5).** SSM Agent runs up to **5 commands in parallel** per instance
  (`Mds.CommandWorkersLimit`, default 5); the service does not serialise `send-command` per
  target either. Two overlapping deploys *will* race `docker compose up`. Keep the GitHub
  concurrency group; note that the default queue depth is 1, so use `queue: max` if every
  master commit must deploy.
- **Dependencies:** the image must be arm64 before any of this works on t4g (sibling research
  ticket); the prod compose file (not in this repo) needs `image: ...:${IMAGE_TAG:-latest}`.

---

## 1. IAM: deploy role

### 1.1 Trust policy

The GitHub OIDC provider in IAM is `token.actions.githubusercontent.com`, audience
`sts.amazonaws.com`. Thumbprints are no longer load-bearing: "AWS secures communication with
OIDC identity providers (IdPs) using our library of trusted root certificate authorities (CAs)
to verify the JSON Web Key Set (JWKS) endpoint's TLS certificate" and falls back to thumbprints
only for non-trusted CAs
(<https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles_providers_create_oidc.html>).

`sub` format. GitHub: "Repositories created after July 15, 2026 use an immutable default
subject format that includes owner and repository IDs" (`repo:octo-org@123456/octo-repo@456789:ref:refs/heads/main`);
older repositories keep `repo:<org>/<repo>:ref:refs/heads/<branch>` unless opted in
(<https://docs.github.com/en/actions/reference/security/oidc>;
`aws-actions/configure-aws-credentials` README says the same and calls the id form
"immutable format (recommended)"). **[verified]** this repo was created 2021-07-20 and
`GET /repos/wiktorkowalski/WojtusDiscord/actions/oidc/customization/sub` returns
`{"use_default":true,"use_immutable_subject":false,"sub_claim_prefix":"repo:wiktorkowalski/WojtusDiscord"}`
— so the legacy string is the one to match. If the repo is ever renamed or the immutable
subject is opted in, the trust policy must change in lockstep.

Reusable-workflow nuance: when a reusable workflow runs, "the subject claim references the
caller's repository context, while the `job_workflow_ref` claim identifies the reusable
workflow" (same GitHub page). The `sub` is therefore `repo:wiktorkowalski/WojtusDiscord:...`
even though the job body lives in `wiktorkowalski/github-workflows` — the trust policy below
works unchanged. IAM only exposes `sub`, `aud`, `amr` as condition keys for a generic OIDC
provider, so pinning `job_workflow_ref` would need GitHub's sub-claim customisation; not worth
it for one repo.

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "GitHubActionsMasterOnly",
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::<ACCOUNT_ID>:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com",
          "token.actions.githubusercontent.com:sub": "repo:wiktorkowalski/WojtusDiscord:ref:refs/heads/master"
        }
      }
    }
  ]
}
```

`StringEquals` on the full `sub` (GitHub's own example uses `StringLike` with
`repo:octo-org/octo-repo:*`, which would also admit PR and tag refs —
<https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/configuring-openid-connect-in-amazon-web-services>).
`pull_request` runs never reach the deploy job anyway (`if: github.event_name != 'pull_request'`),
but the trust policy is the layer that holds if the workflow is edited.

### 1.2 Permissions policy

Shape taken from the two AWS examples: "Example 3: Allow a user to use a specific SSM document
to run commands on specific nodes"
(<https://docs.aws.amazon.com/systems-manager/latest/userguide/security_iam_id-based-policy-examples.html#identity-based-policies-example-3>)
and "Restricting Run Command access based on tags"
(<https://docs.aws.amazon.com/systems-manager/latest/userguide/run-command-setting-up.html#tag-based-access>).
Two facts from those pages shape the JSON:

- "The account ID shouldn't be specified in the Amazon Resource Name (ARN) for AWS public
  documents (documents that begin with `AWS-*`)" → `arn:aws:ssm:eu-central-1::document/AWS-RunShellScript`.
- `SendCommand` authorises against **both** the document and the instance; the tag condition
  goes on the instance statement (`ssm:resourceTag/<key>`). An untagged or mis-tagged target
  returns `AccessDenied` as the invocation status.
- `ListCommandInvocations`/`ListCommands`/`CancelCommand` are shown on `Resource: "*"` in
  Example 3. `GetCommandInvocation` is not resource-scoped either (unverified against the
  Service Authorization Reference this session — that page is a JS shell for `curl`; the AWS
  policy examples never scope it).

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "RunShellScriptOnly",
      "Effect": "Allow",
      "Action": "ssm:SendCommand",
      "Resource": "arn:aws:ssm:eu-central-1::document/AWS-RunShellScript"
    },
    {
      "Sid": "OnlyTaggedInstances",
      "Effect": "Allow",
      "Action": "ssm:SendCommand",
      "Resource": "arn:aws:ec2:eu-central-1:<ACCOUNT_ID>:instance/*",
      "Condition": {
        "StringEquals": {
          "ssm:resourceTag/Service": "wojtusdiscord"
        }
      }
    },
    {
      "Sid": "ReadBackResult",
      "Effect": "Allow",
      "Action": [
        "ssm:GetCommandInvocation",
        "ssm:ListCommandInvocations"
      ],
      "Resource": "*"
    }
  ]
}
```

What this role cannot do: Session Manager (`ssm:StartSession`), any other document
(`AWS-RunPatchBaseline`, `AWS-UpdateSSMAgent`, …), any instance without the tag, anything in
another region. A leaked OIDC token (60 s lifetime, single-use) buys one shell command on one
box, which is exactly what the deploy needs.

Not needed: `aws:ViaAWSService` — the docs require it only "If you use any global condition keys
for the `SendCommand` action" (`aws:SourceVpce` etc.); `ssm:resourceTag` is a service key.
`ec2:DescribeInstanceStatus` / `ssm:DescribeInstanceInformation` are console conveniences.

Tag the instance `Service=wojtusdiscord` at launch (Terraform `tags` or console). Tags are the
thing the deploy pipeline's blast radius keys on, so treat that tag as part of the deploy
contract.

---

## 2. Instance side

### 2.1 SSM Agent + instance profile

- SSM Agent is preinstalled on "Ubuntu Server 18.04, 20.04, 22.04 LTS, 24.04 LTS, 24.0, and
  25.04" AMIs (<https://docs.aws.amazon.com/systems-manager/latest/userguide/ami-preinstalled-agent.html>;
  Canonical ships it as the `amazon-ssm-agent` snap). Check with `snap list amazon-ssm-agent`
  / `systemctl status snap.amazon-ssm-agent.amazon-ssm-agent.service` on first boot.
- Instance profile: attach the AWS managed policy **`AmazonSSMManagedInstanceCore`** — that is
  the policy the "Alternative configuration for EC2 instance permissions" procedure names as
  the one to search for and select
  (<https://docs.aws.amazon.com/systems-manager/latest/userguide/setup-instance-permissions.html#instance-profile-add-permissions>).
  AWS's *recommended* path is Default Host Management Configuration (account-level role, needs
  IMDSv2); for one instance an explicit profile is simpler and shows up in Terraform.
- Runs as root: "On Linux and macOS, SSM Agent runs as the root user"
  (<https://docs.aws.amazon.com/systems-manager/latest/userguide/ssm-agent-technical-details.html>).
  Consequences: `docker compose` needs no group membership; `$HOME` is `/root`, so anything
  that reads `~/.docker/config.json` (a `docker login`) must be done **as root**, not as
  `ubuntu`. `.env` next to the compose file is read by path, so its owner does not matter.
- Outbound: the agent needs HTTPS to `ssm.`, `ec2messages.`, `ssmmessages.eu-central-1.amazonaws.com`.
  A public subnet with a NAT-less default route to an IGW is enough; VPC endpoints are for
  private subnets only.

### 2.2 How the instance pulls from GHCR

**[verified 2026-09-13]** `ghcr.io/wiktorkowalski/wojtusdiscord/discord-event-service` is
already public: an anonymous `GET https://ghcr.io/token?scope=repository:...:pull` returned a
token and `GET /v2/.../manifests/latest` and `/manifests/0bcd622` both returned HTTP 200 with
it. Consistent with the repo being public and packages inheriting the linked repo's
permissions ("By default, if you publish a package that is linked to a repository, the package
automatically inherits the access permissions (but not the visibility) of the linked
repository" —
<https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility>).

| | Public package (status quo) | Read-only classic PAT in `docker login` |
|---|---|---|
| Mechanism | "In the Container registry, public packages allow anonymous access and can be pulled without authentication or signing in via the CLI." (same page) | "GitHub Packages only supports authentication using a personal access token (classic)." Scope: "Select the `read:packages` scope to download container images and read their metadata." (<https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry>) |
| Host state | none | `echo $PAT \| sudo docker login ghcr.io -u wiktorkowalski --password-stdin` once, as **root** (SSM runs as root, see §2.1); credential lands plaintext in `/root/.docker/config.json` |
| Rotation | none | PAT expiry → deploy fails at `pull` with 401 until someone re-logs in; classic PATs cannot be scoped to one package, `read:packages` reads **every** package the user can see |
| Reversibility | "Once you make a package public, you cannot make it private again." (visibility page) — but it already *is* public, so nothing to reverse | reversible |
| Exposure | image layers world-readable — already true today; the image contains compiled app code + the SPA, no secrets (config comes from env vars, `reference_prod_deploy`) | none extra |

**Recommendation: keep the package public, no login on the host.** It is the current state,
zero moving parts, and the only alternative GitHub offers is a user-wide classic PAT sitting
in `/root/.docker/config.json` — a long-lived credential on the very box the map is trying to
keep credential-free. Fine-grained PATs are not accepted by GHCR. The one thing to *not* do is
flip the package private for hygiene reasons: that would force the PAT route. No
pre-flight `docker manifest inspect` step either — a failing pull is the signal.

---

## 3. Second deploy mode in `dotnet-docker-deploy.yml`

### 3.1 Contract

New `workflow_call` surface — three inputs, one secret. The existing `deploy` job and its
inputs (`deploy-compose-path`, `deploy-runner-labels`) are not touched; other services
(thermal-printer, uber-prints) keep calling it exactly as before.

| Name | Kind | Why |
|---|---|---|
| `deploy-ssm-instance-id` | input, default `""` | The gate. Non-empty → `deploy-ssm` job runs. `i-…` ids are not secrets. |
| `deploy-aws-region` | input, default `eu-central-1` | Region of the instance and of the STS/SSM endpoints. |
| `deploy-compose-path` | **existing** input, reused | Working directory on the instance. Same meaning as in self-hosted mode. |
| `AWS_DEPLOY_ROLE_ARN` | **secret**, `required: false` | Role ARN. Technically not secret (account id + role name), but inputs "are visible in logs" and `${{ inputs.* }}` is expression-interpolated into every log line; a secret is masked and, more importantly, cannot be overridden by a PR editing the caller. Delivered by the caller's existing `secrets: inherit` — "Workflows that call reusable workflows in the same organization or enterprise can use the `inherit` keyword to implicitly pass the secrets." (<https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows>). Same-owner personal repos qualify (the current `secrets: inherit` already works for `GITHUB_TOKEN`). |

Ticket asked "secret or input?" for the role ARN → **secret**, declared in
`on.workflow_call.secrets` so the reusable workflow documents it. Job-level `if:` cannot read
the `secrets` context, which is why the gate is the instance-id input and a missing ARN fails
loudly inside `configure-aws-credentials` instead.

`permissions`. "The `GITHUB_TOKEN` permissions passed from the caller workflow can be only
downgraded (not elevated) by the called workflow."
(<https://docs.github.com/en/actions/reference/workflows-and-actions/reusing-workflow-configurations>).
So the reusable workflow declaring `id-token: write` on the job is necessary but not
sufficient — the **caller** (`build.yml`) must also grant it, either at workflow level or on
the `build-deploy` job. GitHub: `id-token: write` "is required for requesting the JWT"
(<https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/configuring-openid-connect-in-amazon-web-services>).

### 3.2 Reusable workflow (additions only)

```yaml
on:
  workflow_call:
    inputs:
      # ...existing inputs unchanged...
      deploy-ssm-instance-id:
        description: EC2 instance id to deploy to via SSM Run Command (empty = skip SSM deploy)
        required: false
        type: string
        default: ""
      deploy-aws-region:
        description: AWS region of the SSM-managed instance
        required: false
        type: string
        default: "eu-central-1"
    secrets:
      AWS_DEPLOY_ROLE_ARN:
        description: IAM role assumed via GitHub OIDC for the SSM deploy (required when deploy-ssm-instance-id is set)
        required: false

jobs:
  build:
    # ...unchanged...

  deploy:
    # ...unchanged (self-hosted mode)...

  deploy-ssm:
    name: Deploy (SSM)
    needs: build
    if: github.event_name != 'pull_request' && inputs.deploy-ssm-instance-id != ''
    runs-on: ubuntu-latest
    timeout-minutes: 20
    permissions:
      id-token: write   # OIDC JWT; caller must grant this too
      contents: read
    env:
      INSTANCE_ID: ${{ inputs.deploy-ssm-instance-id }}
      COMPOSE_PATH: ${{ inputs.deploy-compose-path }}
      IMAGE_TAG: ${{ github.sha }}

    steps:
      - name: Configure AWS credentials (OIDC)
        uses: aws-actions/configure-aws-credentials@v6
        with:
          role-to-assume: ${{ secrets.AWS_DEPLOY_ROLE_ARN }}
          aws-region: ${{ inputs.deploy-aws-region }}
          role-session-name: gha-${{ github.run_id }}-${{ github.run_attempt }}

      - name: Send deploy command
        id: send
        run: |
          set -euo pipefail
          SHORT_SHA="${IMAGE_TAG:0:7}"
          PARAMS=$(jq -cn --arg wd "$COMPOSE_PATH" --arg tag "$SHORT_SHA" '{
            workingDirectory: [$wd],
            executionTimeout: ["600"],
            commands: [
              "set -euo pipefail",
              ("export IMAGE_TAG=" + $tag),
              "flock -w 300 /var/lock/compose-deploy.lock docker compose up -d --pull always --remove-orphans",
              "docker compose ps"
            ]
          }')
          COMMAND_ID=$(aws ssm send-command \
            --instance-ids "$INSTANCE_ID" \
            --document-name AWS-RunShellScript \
            --comment "deploy ${GITHUB_REPOSITORY#*/}@${SHORT_SHA} run=${GITHUB_RUN_ID}" \
            --timeout-seconds 60 \
            --parameters "$PARAMS" \
            --query 'Command.CommandId' --output text)
          echo "command-id=$COMMAND_ID" >> "$GITHUB_OUTPUT"
          echo "Sent $COMMAND_ID to $INSTANCE_ID (tag $SHORT_SHA)"

      - name: Wait for command and relay output
        env:
          COMMAND_ID: ${{ steps.send.outputs.command-id }}
        run: |
          set -uo pipefail
          for i in $(seq 1 180); do            # 180 × 5 s = 15 min ceiling
            RESULT=$(aws ssm get-command-invocation \
              --command-id "$COMMAND_ID" --instance-id "$INSTANCE_ID" \
              --output json 2>/dev/null) || { sleep 5; continue; }   # InvocationDoesNotExist right after send
            STATUS=$(jq -r .Status <<<"$RESULT")
            case "$STATUS" in
              Pending|InProgress|Delayed|Cancelling) sleep 5 ;;
              *) break ;;
            esac
          done
          echo "::group::stdout"; jq -r .StandardOutputContent <<<"${RESULT:-null}"; echo "::endgroup::"
          echo "::group::stderr"; jq -r .StandardErrorContent  <<<"${RESULT:-null}"; echo "::endgroup::"
          echo "Status=$STATUS StatusDetails=$(jq -r .StatusDetails <<<"${RESULT:-null}") ResponseCode=$(jq -r .ResponseCode <<<"${RESULT:-null}")"
          [ "${STATUS:-}" = "Success" ] || { echo "::error::SSM command $COMMAND_ID ended with status ${STATUS:-unknown}"; exit 1; }
```

Notes on each choice, with the primary source:

- **`configure-aws-credentials@v6`** — current major (README: `@v6.2.4`, floating `@v6`). Default
  `role-duration-seconds` 3600; the job needs ~5 min. `role-session-name` shows in CloudTrail
  as `gha-<run>-<attempt>` — useful when reading who deployed what.
- **`--timeout-seconds 60`** is the *delivery* timeout: "If this time is reached and the command
  hasn't already started running, it won't run." (min 30). `executionTimeout` (document
  parameter, default 3600) is how long the script may run once started; 600 s is generous for
  one image pull + restart. Total worst case before AWS gives up is the sum
  (<https://docs.aws.amazon.com/systems-manager/latest/userguide/monitor-commands.html#monitor-about-status-timeouts>),
  hence `timeout-minutes: 20` on the job and the 15-min loop ceiling.
- **Why not `aws ssm wait command-executed`.** Its contract: "It will poll every 5 seconds until
  a successful state has been reached. This will exit with a return code of 255 after 20
  failed checks." (<https://docs.aws.amazon.com/cli/latest/reference/ssm/wait/command-executed.html>)
  — i.e. a hard 100-second ceiling, and exit 255 whether the command *failed* or is merely
  *still pulling*. A cold pull of an aspnet image on a t4g.small can exceed 100 s. The loop
  above terminates on any terminal status and only then inspects it.
- **Terminal statuses** for an invocation: `Success`, `Cancelled`, `TimedOut`, `Failed`
  (plus `DeliveryTimedOut`, `ExecutionTimedOut`, `Undeliverable`, `Terminated`, `AccessDenied`
  in `StatusDetails`); non-terminal: `Pending`, `InProgress`, `Delayed`, `Cancelling`
  (<https://docs.aws.amazon.com/cli/latest/reference/ssm/get-command-invocation.html>, and the
  status table at <https://docs.aws.amazon.com/systems-manager/latest/userguide/monitor-commands.html>).
  `Success` means "returned an exit code of zero" — hence `set -euo pipefail` at the top of
  the remote script so a failed `pull` is a non-zero exit, not a silent success followed by
  `up -d` of the old image.
- **Output size.** `StandardOutputContent` is "The first 24,000 characters written by the
  plugin to `stdout`", `StandardErrorContent` "The first 8,000 characters … to `stderr`"
  (get-command-invocation reference). `docker compose pull` progress goes to stderr and can
  blow 8,000 chars on a cold pull; `up -d --pull always` is terser. If full logs are ever
  needed, `--cloud-watch-output-config CloudWatchOutputEnabled=true,CloudWatchLogGroupName=…`
  plus `logs:CreateLogStream`/`PutLogEvents` on the **instance** profile — deliberately left
  out of the minimum.
- **`flock`** on the host is the belt-and-braces for §5 — cheap, and it makes a manual
  rollback (§4) safe against an in-flight deploy too. `-w 300` fails the invocation instead
  of hanging if a previous deploy is stuck.
- **`IMAGE_TAG`** — see §4; the compose file on the host must reference
  `${IMAGE_TAG:-latest}`.

### 3.3 Caller (`build.yml`) after the move

```yaml
permissions:
  contents: read
  packages: write
  id-token: write        # new: required for the OIDC JWT in deploy-ssm

jobs:
  build-deploy:
    uses: wiktorkowalski/github-workflows/.github/workflows/dotnet-docker-deploy.yml@master
    with:
      image-name: ${{ github.repository }}/discord-event-service
      dotnet-project: src/DiscordEventService/DiscordEventService.csproj
      dockerfile: src/DiscordEventService/Dockerfile
      context: .
      build-args: GIT_SHA=${{ github.sha }}
      deploy-compose-path: /home/ubuntu/docker/wojtusdiscord
      deploy-ssm-instance-id: i-0123456789abcdef0
      deploy-aws-region: eu-central-1
      # deploy-runner-labels: dropped → self-hosted job still gated on deploy-compose-path!
    secrets: inherit
```

**Trap:** the existing `deploy` job is gated on `deploy-compose-path != ''` only, and
`deploy-ssm` reuses `deploy-compose-path` for the working directory. With both set, *both*
jobs run — the self-hosted one hangs forever waiting for a `[self-hosted]` runner that no
longer exists. Fix in the reusable workflow, one line:

```yaml
  deploy:
    if: github.event_name != 'pull_request' && inputs.deploy-compose-path != '' && inputs.deploy-ssm-instance-id == ''
```

That is the only edit to the existing job, and it is a no-op for every current caller (they
leave `deploy-ssm-instance-id` at its `""` default). Alternative: a separate
`deploy-ssm-compose-path` input and leave `deploy` byte-identical; more surface for the same
outcome. Pick the one-line gate.

During the cutover window both the homelab and the EC2 can be deployed from the same run by
*temporarily* not adding that gate — useful for a dual-run soak, then add it.

---

## 4. Rollback recipe

### 4.1 What tags exist

`actions/docker-build-push/action.yml` (`docker/metadata-action@v6`):

```yaml
tags: |
  type=raw,value=latest,enable={{is_default_branch}}
  type=sha,prefix=
```

`type=sha` → "Output Git short commit (or long if specified) as Docker tag like
`sha-860c190`" — 7 characters by default; `prefix=` (empty) strips the `sha-`
(<https://github.com/docker/metadata-action>). So every build, PR builds included (push is
false for PRs, so only master builds actually land), tags `ghcr.io/wiktorkowalski/wojtusdiscord/discord-event-service:<7-char-sha>`,
and master builds also move `:latest`. **[verified]** `:0bcd622` (current master head) exists
on GHCR; `:sha-0bcd622` does not (404); the tag list is 100+ entries deep, so old shas stay
pullable.

### 4.2 Make the compose file sha-addressable

On the host, `/home/ubuntu/docker/wojtusdiscord/docker-compose.yml` (not in this repo) — one
line:

```yaml
services:
  discord-event-service:
    image: ghcr.io/wiktorkowalski/wojtusdiscord/discord-event-service:${IMAGE_TAG:-latest}
```

The deploy job (§3.2) exports `IMAGE_TAG=<sha>` before `docker compose up -d --pull always`.
`--pull always` makes the separate `docker compose pull` unnecessary and — unlike the homelab
recipe — does **not** pull a newer `postgres:18` as a side effect of deploying the bot
(that side effect is what turned #326's triple restart into a Postgres restart too). Postgres
image bumps become a deliberate `docker compose pull postgres && up -d postgres`.

### 4.3 Roll back

From a laptop with an IAM identity that has the same `ssm:SendCommand` grant (or from a
`workflow_dispatch` job with an `image-tag` input that reuses §3.2 — recommended follow-up,
because then rollback also goes through the audited OIDC role and the `flock`):

```bash
aws ssm send-command --region eu-central-1 \
  --instance-ids i-0123456789abcdef0 \
  --document-name AWS-RunShellScript \
  --comment "rollback to 7f8b9c2" \
  --parameters '{"workingDirectory":["/home/ubuntu/docker/wojtusdiscord"],"executionTimeout":["600"],
    "commands":["set -euo pipefail","export IMAGE_TAG=7f8b9c2",
      "flock -w 300 /var/lock/compose-deploy.lock docker compose up -d --pull always",
      "docker compose ps"]}'
# then poll get-command-invocation as in §3.2, or:
aws ssm list-command-invocations --command-id <id> --details --query 'CommandInvocations[0].CommandPlugins[0].[Status,Output]' --output text
```

Pick the sha from `git log --oneline master` or from `/health` (which reports the running
commit since #193). The rollback is *not* sticky: the next master push deploys its own sha.
To pin for longer, write `IMAGE_TAG=7f8b9c2` into the host `.env` (compose reads it for
interpolation) — the job's `export` then wins only during that job's invocation, so
**remove the line from `.env` after the fix ships** or the next deploy silently keeps the old
tag. Prefer the short-lived form.

Migrations: rolling the image back does not roll the schema back (`Database__AutoMigrate`
applies forward only). Same constraint as today; the migration-safety rule in agent memory
`feedback_no_data_loss` applies before every deploy, not after.

---

## 5. Does #326 (concurrency group) survive the move?

**Yes — keep it.** Nothing in the SSM path serialises deploys:

- SSM Agent runs commands **concurrently**: `Mds.CommandWorkersLimit` — "Allow this number of
  commands to run in parallel", default **5**
  (<https://github.com/aws/amazon-ssm-agent#config-property-definitions>). Two
  `send-command`s arriving 20 s apart both start; the second `docker compose up` runs while the
  first is mid-pull. Same restart-churn failure mode as 2026-08-16, now on a smaller box.
- `send-command` has `--max-concurrency` / `--max-errors`, but those bound *invocations across
  targets within one command*, not commands across time
  (<https://docs.aws.amazon.com/cli/latest/reference/ssm/send-command.html>).
- The `flock` in §3.2 turns the race into serialisation **on the host** (second command waits
  ≤ 300 s then runs). That prevents the interleaved-restart mess but still deploys N then N+1
  back-to-back, and a third would fail on the lock timeout.

What *does* change with sha-pinned deploys (§4): the "older build pushed `:latest` last, final
deploy shipped a stale image with no signal" hazard disappears — each run deploys the sha it
built. So #326's remaining purpose is restart-churn avoidance and ordering, which is exactly
what the GitHub group gives:

```yaml
concurrency:
  group: build-deploy-${{ github.ref }}
  cancel-in-progress: false
  queue: max        # see below
```

Correction to #326's text: "Whole runs then queue FIFO … build+push+deploy of commit N
completes before N+1 starts" is only half right by default. GitHub: "`single` (default): At most
one job or workflow run can be pending in the concurrency group. When a new job or workflow
run is queued, any existing pending job or workflow run in the same group is canceled and
replaced." Three quick merges → run 1 executes, run 2 is **cancelled**, run 3 runs. Final
state is still correct (run 3 is a full master build), but "every master commit still deploys"
needs `queue: max` — "Up to 100 jobs or workflow runs can be pending in the concurrency
group." Runs "are processed in first-in-first-out (FIFO) order according to the time each one
started waiting" (<https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#concurrency>).
Whether every intermediate commit *needs* to hit prod is a taste call; `single` is fewer
restarts, `max` is a full audit trail on `/health`.

The concurrency group belongs on the **caller** (`build.yml`); "reusable workflow's jobs are
inside the run" so a workflow-level group on the caller covers the called `deploy-ssm` job.
Don't put a group with `cancel-in-progress: true` on the called side with the same name — the
docs warn that cancels the caller.

---

## Open questions / not verified

1. **Service Authorization Reference rows** for `GetCommandInvocation`/`ListCommandInvocations`
   (resource-scoped or `*`) — the SAR page is a JS redirect shell; policy in §1.2 follows AWS's
   own examples, which use `*`. IAM Access Analyzer's policy validation on paste will settle it
   in seconds.
2. **`secrets: inherit` across two personal repos** — documented for "the same organization or
   enterprise"; it already delivers `GITHUB_TOKEN` here, and a named repo secret is the same
   mechanism, but the first run is the proof.
3. **Ubuntu 24.04 arm64 AMI ships the snap agent enabled** — AWS lists 24.04 LTS; not checked
   on the arm64 image specifically. `snap list` on first boot.
4. **Cold-pull duration on t4g.small** — sets whether `executionTimeout: 600` is generous or
   tight. Measure on the first real deploy; the loop ceiling is 15 min regardless.
5. **arm64 image** — prerequisite from the sibling research ticket; `--pull always` will fail
   with "no matching manifest for linux/arm64" until the build pushes a multi-arch or arm64
   image.

## Sources

- GitHub — Configuring OpenID Connect in AWS:
  <https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/configuring-openid-connect-in-amazon-web-services>
- GitHub — OpenID Connect reference (sub formats, reusable-workflow claims, immutable subject):
  <https://docs.github.com/en/actions/reference/security/oidc>
- GitHub — Reusing workflow configurations (token permissions only downgradable, concurrency warning):
  <https://docs.github.com/en/actions/reference/workflows-and-actions/reusing-workflow-configurations>
- GitHub — Reuse workflows (`secrets: inherit`): <https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows>
- GitHub — Workflow syntax, `concurrency` / `queue`: <https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#concurrency>
- GitHub — Container registry auth (classic PAT, `read:packages`, anonymous public pulls):
  <https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry>
- GitHub — Package access control and visibility:
  <https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility>
- GitHub REST — `GET /repos/{owner}/{repo}/actions/oidc/customization/sub` (queried live)
- aws-actions/configure-aws-credentials README (v6, trust policy, sub formats):
  <https://github.com/aws-actions/configure-aws-credentials/blob/main/README.md>
- AWS IAM — Create an OIDC identity provider (thumbprint note):
  <https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles_providers_create_oidc.html>
- AWS SSM — Identity-based policy examples, Example 3:
  <https://docs.aws.amazon.com/systems-manager/latest/userguide/security_iam_id-based-policy-examples.html>
- AWS SSM — Setting up Run Command / tag-based restriction:
  <https://docs.aws.amazon.com/systems-manager/latest/userguide/run-command-setting-up.html>
- AWS SSM — Instance permissions (`AmazonSSMManagedInstanceCore`):
  <https://docs.aws.amazon.com/systems-manager/latest/userguide/setup-instance-permissions.html>
- AWS SSM — AMIs with SSM Agent preinstalled:
  <https://docs.aws.amazon.com/systems-manager/latest/userguide/ami-preinstalled-agent.html>
- AWS SSM — SSM Agent technical details (runs as root):
  <https://docs.aws.amazon.com/systems-manager/latest/userguide/ssm-agent-technical-details.html>
- AWS SSM — Understanding command statuses and timeouts:
  <https://docs.aws.amazon.com/systems-manager/latest/userguide/monitor-commands.html>
- AWS SSM — Command document plugin reference (`aws:runShellScript`, `workingDirectory`, `timeoutSeconds`):
  <https://docs.aws.amazon.com/systems-manager/latest/userguide/documents-command-ssm-plugin-reference.html>
- AWS CLI — `ssm send-command`: <https://docs.aws.amazon.com/cli/latest/reference/ssm/send-command.html>
- AWS CLI — `ssm get-command-invocation`: <https://docs.aws.amazon.com/cli/latest/reference/ssm/get-command-invocation.html>
- AWS CLI — `ssm wait command-executed`: <https://docs.aws.amazon.com/cli/latest/reference/ssm/wait/command-executed.html>
- amazon-ssm-agent README, config property definitions (`CommandWorkersLimit`):
  <https://github.com/aws/amazon-ssm-agent#config-property-definitions>
- docker/metadata-action (`type=sha`, `{{is_default_branch}}`): <https://github.com/docker/metadata-action>
- Repo: `.github/workflows/build.yml`; `wiktorkowalski/github-workflows` —
  `.github/workflows/dotnet-docker-deploy.yml`, `actions/docker-build-push/action.yml` (read at `master`, 2026-09-13)
