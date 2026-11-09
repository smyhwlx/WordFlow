using WordFlow.App.Bootstrap;
using WordFlow.App.ViewModels;
using WordFlow.Application.Learning;
using WordFlow.Application.Ports;
using WordFlow.Application.Relations;
using WordFlow.Application.Shortcuts;
using WordFlow.Domain.Learning;
using WordFlow.Infrastructure.Audio;
using WordFlow.Infrastructure.Data;

namespace WordFlow.App.Tests.ViewModels;

public sealed class VocabularySuggestionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"wordflow-suggest-{Guid.NewGuid():N}");

    [Fact]
    public void Matcher_caps_candidates_at_twenty_ordered_by_frequency()
    {
        var words = Enumerable.Range(1, 26)
            .Select(rank => Word($"sa{rank:D2}", rank))
            .ToArray();

        var matches = VocabularyPrefixMatcher.Match(words, "sa");

        Assert.Equal(20, matches.Count);
        Assert.Equal("sa01", matches[0].Lemma);
        Assert.Equal("sa20", matches[^1].Lemma);
    }

    [Fact]
    public void Matcher_narrows_every_time_another_letter_is_typed()
    {
        var words = new[] { Word("say", 19), Word("safe", 1058), Word("safety", 1132), Word("sail", 4124), Word("sad", 2145) };

        Assert.Equal(new[] { "say", "safe", "safety", "sad", "sail" }, Lemmas(VocabularyPrefixMatcher.Match(words, "sa")));
        Assert.Equal(new[] { "safe", "safety" }, Lemmas(VocabularyPrefixMatcher.Match(words, "saf")));
        Assert.Equal(new[] { "safety" }, Lemmas(VocabularyPrefixMatcher.Match(words, "safet")));
    }

    [Fact]
    public void Matcher_ignores_case()
    {
        var words = new[] { Word("say", 19), Word("safe", 1058) };

        Assert.Equal(Lemmas(VocabularyPrefixMatcher.Match(words, "sa")), Lemmas(VocabularyPrefixMatcher.Match(words, "SA")));
    }

    [Fact]
    public void Matcher_returns_nothing_for_a_misspelling()
    {
        var words = new[] { Word("say", 19), Word("safe", 1058) };

        Assert.Empty(VocabularyPrefixMatcher.Match(words, "sya"));
        Assert.Empty(VocabularyPrefixMatcher.Match(words, "   "));
    }

    [Fact]
    public async Task Typing_a_prefix_opens_a_dropdown_of_at_most_twenty_candidates()
    {
        using var viewModel = CreateViewModel(BuildCorpus());

        viewModel.VocabularyQuery = "sa";
        await viewModel.VocabularySuggestionRefresh;

        Assert.True(viewModel.IsVocabularySuggestionOpen);
        Assert.Equal(20, viewModel.VocabularySuggestions.Count);
        Assert.All(viewModel.VocabularySuggestions, item => Assert.StartsWith("sa", item.Lemma, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("say", viewModel.VocabularySuggestions[0].Lemma);

        viewModel.VocabularyQuery = "saf";
        await viewModel.VocabularySuggestionRefresh;

        Assert.All(viewModel.VocabularySuggestions, item => Assert.StartsWith("saf", item.Lemma, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Misspelled_prefix_leaves_the_dropdown_closed()
    {
        using var viewModel = CreateViewModel(BuildCorpus());

        viewModel.VocabularyQuery = "sya";
        await viewModel.VocabularySuggestionRefresh;

        Assert.False(viewModel.IsVocabularySuggestionOpen);
        Assert.Empty(viewModel.VocabularySuggestions);
    }

    [Fact]
    public async Task Picking_a_candidate_fills_the_box_closes_the_dropdown_and_searches()
    {
        using var viewModel = CreateViewModel(BuildCorpus());
        viewModel.VocabularyQuery = "saf";
        await viewModel.VocabularySuggestionRefresh;

        await viewModel.CommitVocabularySuggestionAsync(viewModel.VocabularySuggestions[0]);

        Assert.Equal("safe", viewModel.VocabularyQuery);
        Assert.False(viewModel.IsVocabularySuggestionOpen);
        Assert.Empty(viewModel.VocabularySuggestions);
        Assert.Contains(viewModel.VocabularyResults, item => item.Lemma == "safe");
    }

    [Fact]
    public async Task Picking_a_candidate_loads_its_confusable_relations()
    {
        using var viewModel = CreateViewModel(BuildCorpus());
        viewModel.ConfusableQuery = "saf";
        await viewModel.ConfusableSuggestionRefresh;

        var picked = viewModel.ConfusableSuggestions.Single(item => item.Lemma == "safe");
        await viewModel.CommitConfusableSuggestionAsync(picked);

        Assert.Equal("safe", viewModel.ConfusableQuery);
        Assert.False(viewModel.IsConfusableSuggestionOpen);
        Assert.Contains(viewModel.ConfusableResults, item => item.Spelling == "safety");
    }

    private static IReadOnlyList<VocabularyWord> BuildCorpus()
    {
        // 23 headwords share the "sa" prefix so the 20-candidate cap is exercised by real matching.
        var lemmas = new[]
        {
            "say", "same", "save", "safe", "safety", "sample", "sad", "sail",
            "sabotage", "sack", "sacred", "sacrifice", "saddle", "sake", "salad", "salary",
            "salmon", "salon", "salt", "salute", "sand", "sandwich", "satellite",
        };
        return lemmas.Select((lemma, index) => Word(lemma, 100 + index)).ToArray();
    }

    private static VocabularyWord Word(string lemma, int rank) => new(Guid.NewGuid(), lemma, rank, true, "", $"{lemma} 的释义");

    private static string[] Lemmas(IReadOnlyList<VocabularyWord> words) => words.Select(word => word.Lemma).ToArray();

    private ControlCenterViewModel CreateViewModel(IReadOnlyList<VocabularyWord> words)
    {
        Directory.CreateDirectory(directory);
        var paths = new AppPaths(directory, Path.Combine(directory, "bundled"));
        paths.Initialize();
        var factory = new SqliteConnectionFactory(paths.UserDatabasePath);
        new MigrationRunner(factory).MigrateAsync(default).GetAwaiter().GetResult();
        var settingsStore = new SqliteAppSettingStore(factory);
        var vocabulary = new FakeVocabularyRepository(words);
        var relations = new FakeRelationRepository(words);
        using var pronunciationService = new FakePronunciationService();
        return new ControlCenterViewModel(
            new DailyPlanSettingsViewModel(settingsStore),
            new ShortcutSettingsViewModel(new FakeShortcutService()),
            new PronunciationSettingsViewModel(pronunciationService, settingsStore),
            new ThemeSettingsViewModel(new ImageThemeService(paths, settingsStore)),
            new MutableProgressReader(),
            vocabulary: vocabulary,
            getConfusables: new GetConfusables(relations, vocabulary, TimeProvider.System),
            clock: new FixedTimeProvider(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero)));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed class FakeVocabularyRepository(IReadOnlyList<VocabularyWord> words) : IVocabularyRepository
    {
        public Task<Page<VocabularyWord>> GetWordsAsync(PageRequest page, CancellationToken ct)
        {
            var slice = words.Skip(page.Offset).Take(page.Limit).ToArray();
            return Task.FromResult(new Page<VocabularyWord>(slice, words.Count, page.Offset + slice.Length < words.Count, $"vocabulary:{words.Count}"));
        }

        public Task<ExhaustionProbe> ProbeWordsEndAsync(int offset, string snapshotId, CancellationToken ct) =>
            Task.FromResult(new ExhaustionProbe(offset >= words.Count, snapshotId));

        public Task<VocabularyWord?> GetWordAsync(Guid wordId, CancellationToken ct) =>
            Task.FromResult(words.FirstOrDefault(word => word.WordId == wordId));

        public Task<Page<VocabularySense>> GetSensesAsync(Guid wordId, PageRequest page, CancellationToken ct) =>
            Task.FromResult(new Page<VocabularySense>([], 0, false, $"senses:{wordId:D}:0"));

        public Task<ExhaustionProbe> ProbeSensesEndAsync(Guid wordId, int offset, string snapshotId, CancellationToken ct) =>
            Task.FromResult(new ExhaustionProbe(true, snapshotId));
    }

    private sealed class FakeRelationRepository(IReadOnlyList<VocabularyWord> words) : IRelationRepository
    {
        public Task<Page<WordRelation>> GetRelationsAsync(Guid sourceWordId, PageRequest page, CancellationToken ct)
        {
            if (page.Offset > 0) return Task.FromResult(new Page<WordRelation>([], 1, false, "relations:1"));
            var target = words.Single(word => word.Lemma == "safety");
            var relation = new WordRelation(sourceWordId, target.WordId, RelationKinds.SpellingSimilar, RelationDirection.Bidirectional, Contrast: "safe 是形容词，safety 是名词");
            return Task.FromResult(new Page<WordRelation>([relation], 1, false, "relations:1"));
        }

        public Task<ExhaustionProbe> ProbeRelationsEndAsync(Guid sourceWordId, int offset, string snapshotId, CancellationToken ct) =>
            Task.FromResult(new ExhaustionProbe(true, snapshotId));

        public Task<Page<MisspellingRelation>> GetMisspellingsAsync(Guid targetWordId, PageRequest page, CancellationToken ct) =>
            Task.FromResult(new Page<MisspellingRelation>([], 0, false, "misspellings:0"));

        public Task<ExhaustionProbe> ProbeMisspellingsEndAsync(Guid targetWordId, int offset, string snapshotId, CancellationToken ct) =>
            Task.FromResult(new ExhaustionProbe(true, snapshotId));

        public Task SetOverrideAsync(UserRelationOverride relationOverride, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class MutableProgressReader : ILearningProgressReader
    {
        public Task<DailyLearningStatistics> GetDailyStatisticsAsync(DateOnly day, CancellationToken ct) =>
            Task.FromResult(new DailyLearningStatistics(0, 0));
    }

    private sealed class FakePronunciationService : IPronunciationService
    {
        public IReadOnlyList<PronunciationVoice> Voices { get; } = [new("test", "Test", "en-US", PronunciationAccent.American)];
        public PronunciationAvailability Availability { get; } = new(true, "可用", PronunciationInventoryState.AuthoritativeAvailable);
        public PronunciationVoice? SelectVoice(string? voiceId, PronunciationAccent preference) => Voices[0];
        public Task<PronunciationPlaybackResult> SpeakAsync(string text, string voiceId, int rate, int volume, CancellationToken ct) =>
            Task.FromResult(PronunciationPlaybackResult.Completed(text));
        public Task CancelAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeShortcutService : IShortcutService
    {
        public IReadOnlyDictionary<ShortcutAction, ShortcutBinding> Bindings { get; } = ShortcutDefaults.All;
        public IReadOnlyList<ShortcutRestoreIssue> RestoreIssues => [];
        public ShortcutLifecycleSnapshot Lifecycle => new(ShortcutLifecycleState.Ready);
        public event EventHandler<ShortcutAction>? ActionInvoked { add { } remove { } }
        public event EventHandler<ShortcutBindingChangedEventArgs>? BindingChanged { add { } remove { } }
        public event EventHandler<ShortcutCallbackFaultedEventArgs>? CallbackFaulted { add { } remove { } }
        public ShortcutRegistrationResult TryReplace(ShortcutAction action, ShortcutBinding candidate) => ShortcutRegistrationResult.Success(candidate);
        public ShortcutRegistrationResult ResetAll() => ShortcutRegistrationResult.Success();
        public ShortcutRestoreResult RestorePersisted() => new([]);
        public ShortcutRegistrationResult AttachWindowHandle(nint handle) => ShortcutRegistrationResult.Success();
        public bool ProcessWindowMessage(int message, nint id, nint chordData) => false;
        public void Dispose() { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
