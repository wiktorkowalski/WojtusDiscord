using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class MemeMetadataSanitizerTests
{
    [Fact]
    public void Sanitize_CutoutWithPeople_DropsEveryPerson()
    {
        var metadata = Cutout(people: ["Jan Kowalski", "Anna Nowak"], tags: ["emotka"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Empty(sanitized.People);
    }

    // Every way a tag can spell the name: in full, one word of it, and the words glued together.
    [Theory]
    [InlineData("Jan Kowalski")]
    [InlineData("jan kowalski")]
    [InlineData("kowalski jan")]
    [InlineData("kowalski")]
    [InlineData("jan")]
    [InlineData("jan_kowalski")]
    [InlineData("jan-kowalski")]
    [InlineData("jankowalski")]
    [InlineData("kowalski emote")]
    public void Sanitize_CutoutWithPeople_RemovesATagThatNamesThePerson(string nameTag)
    {
        var metadata = Cutout(people: ["Jan Kowalski"], tags: ["emotka", nameTag, "twarz"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["emotka", "twarz"], sanitized.Tags);
    }

    // A short name is matched as a whole word only. A cut on a shared prefix would take ordinary
    // words with it.
    [Fact]
    public void Sanitize_CutoutWithPeople_KeepsATagThatOnlySharesLettersWithTheName()
    {
        var metadata = Cutout(people: ["Jan Kowalski"], tags: ["janusz", "jana", "kowal", "styczeń january"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["janusz", "jana", "kowal", "styczeń january"], sanitized.Tags);
    }

    // Polish inflects names, and the trigram side of search finds those forms: a name of 5 or
    // more letters plus a short ending names the person too. The last two rows sit on the
    // limits: the shortest such name with one more letter, and with the longest ending (4).
    [Theory]
    [InlineData("Krzysztof Gonciarz", "gonciarza")]
    [InlineData("Krzysztof Gonciarz", "mem z gonciarzem")]
    [InlineData("Krzysztof Gonciarz", "krzysztofowi")]
    [InlineData("Jan Kowalski", "kowalskiego")]
    [InlineData("Jan Kowalski", "mina Kowalskiemu")]
    [InlineData("Adam Nowak", "nowaka")]
    [InlineData("Adam Nowak", "nowakowie")]
    public void Sanitize_CutoutWithPeople_RemovesAnInflectedFormOfALongName(string personName, string nameTag)
    {
        var metadata = Cutout(people: [personName], tags: ["emotka", nameTag]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["emotka"], sanitized.Tags);
    }

    // The other side of the limits. An ending of 5 letters or more makes another word
    // ("nowakowski" is a surname of its own), and a 4-letter name has no inflection rule at all.
    [Theory]
    [InlineData("nowakowski")]
    [InlineData("nowakowskiego")]
    [InlineData("adama")]
    public void Sanitize_CutoutWithPeople_KeepsAWordPastTheInflectionLimits(string tag)
    {
        var metadata = Cutout(people: ["Adam Nowak"], tags: [tag, "emotka"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal([tag, "emotka"], sanitized.Tags);
    }

    // A 2-letter name word is too common to be a name on its own, also as the first word.
    [Fact]
    public void Sanitize_NameWordOfTwoLetters_DoesNotRemoveATagEqualToIt()
    {
        var metadata = Cutout(people: ["Xi Jinping"], tags: ["xi", "emotka"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["xi", "emotka"], sanitized.Tags);
    }

    // A name part with a hyphen or an apostrophe is also written as one word. The name words
    // are split on any whitespace: a no-break space or a tab must not glue the whole name.
    [Theory]
    [InlineData("Janusz Korwin-Mikke", "korwinmikke")]
    [InlineData("Janusz Korwin-Mikke", "korwin-mikke")]
    [InlineData("Conan O'Brien", "obrien")]
    [InlineData("Janusz\u00A0Korwin-Mikke", "korwinmikke")]
    [InlineData("Janusz\tKorwin-Mikke", "korwinmikke")]
    public void Sanitize_CutoutWithPeople_RemovesAGluedNamePart(string personName, string nameTag)
    {
        var metadata = Cutout(people: [personName], tags: ["emotka", nameTag]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["emotka"], sanitized.Tags);
    }

    // Search folds case and accents (f_unaccent), so the rule has to fold them too. The Polish ł
    // has no Unicode decomposition: it needs the rule's own mapping.
    [Theory]
    [InlineData("Łukasz Żółć", "lukasz")]
    [InlineData("Łukasz Żółć", "ZOLC")]
    [InlineData("Łukasz Żółć", "łukasz żółć")]
    [InlineData("Łukasz Żółć", "LUKASZ_ZOLC")]
    [InlineData("Lukasz Zolc", "łukasz")]
    [InlineData("Lukasz Zolc", "Żółć")]
    public void Sanitize_CutoutWithPeople_MatchesTheNameWithoutCaseAndAccents(string personName, string nameTag)
    {
        var metadata = Cutout(people: [personName], tags: [nameTag, "emotka"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["emotka"], sanitized.Tags);
    }

    // "z" in "Hed z TVGry" is a preposition, not a name: alone it must not take the tag "z".
    [Fact]
    public void Sanitize_NameWordShorterThanThreeCharacters_DoesNotRemoveATagEqualToIt()
    {
        var metadata = Cutout(people: ["Hed z TVGry"], tags: ["z", "hed", "tvgry", "emotka"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["z", "emotka"], sanitized.Tags);
    }

    [Fact]
    public void Sanitize_NameWithAShortWord_StillRemovesTheFullNameTag()
    {
        var metadata = Cutout(people: ["Hed z TVGry"], tags: ["hed z tvgry", "emotka"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["emotka"], sanitized.Tags);
    }

    [Fact]
    public void Sanitize_CutoutWithTwoPeople_RemovesTheTagsOfBoth()
    {
        var metadata = Cutout(people: ["Jan Kowalski", "Anna Nowak"], tags: ["kowalski", "emotka", "nowak", "anna"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["emotka"], sanitized.Tags);
    }

    // templates and search_phrases are weight A in search, the same as tags.
    [Fact]
    public void Sanitize_CutoutWithPeople_RemovesTemplatesThatNameThePerson()
    {
        var metadata = Cutout(people: ["Jan Kowalski"], tags: []) with { Templates = ["kowalski", "wojak", "Jan Kowalski face"] };

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["wojak"], sanitized.Templates);
    }

    [Fact]
    public void Sanitize_CutoutWithPeople_RemovesSearchPhrasesThatNameThePerson()
    {
        var metadata = Cutout(people: ["Jan Kowalski"], tags: []) with
        {
            SearchPhrases = ["kowalski emotka", "śmieszna mina", "jan się śmieje", "wielki uśmiech"],
        };

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["śmieszna mina", "wielki uśmiech"], sanitized.SearchPhrases);
    }

    // The same instance is the signal the writer reads: nothing dropped, keep the model's verbatim output.
    [Theory]
    [InlineData(MemeImageKind.TemplateMeme)]
    [InlineData(MemeImageKind.ScreenshotPostOrChat)]
    [InlineData(MemeImageKind.Comic)]
    [InlineData(MemeImageKind.PhotoWithCaption)]
    [InlineData(MemeImageKind.EditedPhoto)]
    [InlineData(MemeImageKind.VideoFrame)]
    [InlineData(MemeImageKind.Other)]
    [InlineData(null)]
    public void Sanitize_NotACutout_ReturnsTheSameInstanceWithPeopleAndTags(MemeImageKind? imageKind)
    {
        var metadata = Cutout(people: ["Jan Kowalski"], tags: ["kowalski", "emotka"]) with { ImageKind = imageKind };

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Same(metadata, sanitized);
        Assert.Equal("Jan Kowalski", Assert.Single(sanitized.People).Name);
        Assert.Equal(["kowalski", "emotka"], sanitized.Tags);
    }

    [Fact]
    public void Sanitize_CutoutWithoutPeople_ReturnsTheSameInstance()
    {
        var metadata = Cutout(people: [], tags: ["kowalski", "emotka"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Same(metadata, sanitized);
    }

    // The documented limit: prose is not rewritten. The prompt forbids the name there.
    [Fact]
    public void Sanitize_CutoutWithPeople_LeavesDescriptionsAndOcrTextAsTheyAre()
    {
        var metadata = Cutout(people: ["Jan Kowalski"], tags: ["kowalski"]) with
        {
            DescriptionPl = "Jan Kowalski robi minę",
            DescriptionEn = "Jan Kowalski makes a face",
            OcrText = "KOWALSKI",
        };

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal("Jan Kowalski robi minę", sanitized.DescriptionPl);
        Assert.Equal("Jan Kowalski makes a face", sanitized.DescriptionEn);
        Assert.Equal("KOWALSKI", sanitized.OcrText);
    }

    // franchise is weight A, like the tags: a name there is the same false hit.
    [Fact]
    public void Sanitize_CutoutWithPeople_ClearsAFranchiseThatNamesThePerson()
    {
        var metadata = Cutout(people: ["Jan Kowalski"], tags: ["emotka"]) with { Franchise = "Kowalski Show" };

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Null(sanitized.Franchise);
    }

    [Fact]
    public void Sanitize_CutoutWithPeople_KeepsAFranchiseThatDoesNotNameThePerson()
    {
        var metadata = Cutout(people: ["Jan Kowalski"], tags: ["emotka"]) with { Franchise = "Minecraft" };

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal("Minecraft", sanitized.Franchise);
    }

    // Letters that Unicode does not split into a base letter and a mark (Postgres unaccent maps
    // them too), a dotted capital I, and full-width letters.
    [Theory]
    [InlineData("Søren Nielsen", "soren")]
    [InlineData("Jürgen Groß", "gross")]
    [InlineData("Đorđe Balašević", "dorde")]
    [InlineData("İlker Kaya", "ilker")]
    [InlineData("Ｊａｎ Kowalski", "jan")]
    [InlineData("Işık Yılmaz", "isik")]
    [InlineData("Guðmundur Þórsson", "gudmundur")]
    [InlineData("Guðmundur Þórsson", "thorsson")]
    [InlineData("Æsa Larsen", "aesa")]
    [InlineData("Œdipe Roi", "oedipe")]
    public void Sanitize_CutoutWithPeople_FoldsLettersThatDoNotDecomposeIntoABaseLetter(string personName, string nameTag)
    {
        var metadata = Cutout(people: [personName], tags: ["emotka", nameTag]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["emotka"], sanitized.Tags);
    }

    // JSON can carry U+FFFE or a lone surrogate, and string.Normalize throws on both. The code
    // unit is a number here: neither is safe inside a test name.
    [Theory]
    [InlineData(0xFFFE)]
    [InlineData(0xD800)]
    public void Sanitize_InvalidUnicodeInATag_StillRemovesTheNameTag(int codeUnit)
    {
        var invalid = (char)codeUnit;
        var metadata = Cutout(people: ["Jan Kowalski"], tags: [$"kowalski{invalid}", $"emotka{invalid}"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal([$"emotka{invalid}"], sanitized.Tags);
    }

    [Theory]
    [InlineData(0xFFFE)]
    [InlineData(0xD800)]
    public void Sanitize_InvalidUnicodeInThePersonName_StillRemovesTheNameTag(int codeUnit)
    {
        var metadata = Cutout(people: [$"Jan{(char)codeUnit} Kowalski"], tags: ["kowalski", "jan", "emotka"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["emotka"], sanitized.Tags);
    }

    [Fact]
    public void Sanitize_CutoutWithPeople_KeepsImageKindSourceAndLanguage()
    {
        var metadata = Cutout(people: ["Jan Kowalski"], tags: ["kowalski"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(
            (MemeImageKind.CutoutFaceOrEmote, "discord", MemeLanguage.None),
            (sanitized.ImageKind, sanitized.Source, sanitized.Language));
    }

    private static MemeMetadata Cutout(string[] people, string[] tags) => new MemeMetadata
    {
        DescriptionPl = "Wycięta twarz z szerokim uśmiechem",
        DescriptionEn = "A cut-out face with a wide smile",
        OcrText = "",
        Tags = tags,
        ImageKind = MemeImageKind.CutoutFaceOrEmote,
        Templates = [],
        People = [.. people.Select(name => new MemePerson { Name = name, Evidence = MemePersonEvidence.WidelyRecognized })],
        SearchPhrases = [],
        Franchise = null,
        Source = "discord",
        Language = MemeLanguage.None,
    };
}
