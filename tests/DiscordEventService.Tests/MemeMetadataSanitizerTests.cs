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
    // more letters plus a short ending names the person too.
    [Theory]
    [InlineData("Krzysztof Gonciarz", "gonciarza")]
    [InlineData("Krzysztof Gonciarz", "mem z gonciarzem")]
    [InlineData("Krzysztof Gonciarz", "krzysztofowi")]
    [InlineData("Jan Kowalski", "kowalskiego")]
    [InlineData("Jan Kowalski", "mina Kowalskiemu")]
    public void Sanitize_CutoutWithPeople_RemovesAnInflectedFormOfALongName(string personName, string nameTag)
    {
        var metadata = Cutout(people: [personName], tags: ["emotka", nameTag]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["emotka"], sanitized.Tags);
    }

    // The ending is short. A longer word that only starts with the name is another word.
    [Fact]
    public void Sanitize_CutoutWithPeople_KeepsAWordThatIsMuchLongerThanTheName()
    {
        var metadata = Cutout(people: ["Adam Nowak"], tags: ["nowakowskiego", "emotka"]);

        var sanitized = MemeMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(["nowakowskiego", "emotka"], sanitized.Tags);
    }

    // A name part with a hyphen or an apostrophe is also written as one word.
    [Theory]
    [InlineData("Janusz Korwin-Mikke", "korwinmikke")]
    [InlineData("Janusz Korwin-Mikke", "korwin-mikke")]
    [InlineData("Conan O'Brien", "obrien")]
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
    public void Sanitize_CutoutWithPeople_FoldsLettersThatDoNotDecomposeIntoABaseLetter(string personName, string nameTag)
    {
        var metadata = Cutout(people: [personName], tags: ["emotka", nameTag]);

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
