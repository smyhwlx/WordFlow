using WordFlow.Application.Ports;
using WordFlow.Application.Relations;

namespace WordFlow.App.ViewModels;

/// <summary>
/// Chinese display labels for relation kinds. The corpus stores machine names like
/// "spelling_similar"; the control center should never leak them to the user.
/// </summary>
public static class RelationTypeLabels
{
    public static string Describe(string relationType) => relationType switch
    {
        RelationKinds.SpellingSimilar => "形近易混",
        RelationKinds.PronunciationSimilar => "音近易混",
        RelationKinds.RootConfusable => "同词根易混",
        RelationKinds.TopicConfusable => "同主题易混",
        RelationKinds.AntonymConfusable => "反义易混",
        RelationKinds.PersonalConfusable => "个人易混",
        RelationKinds.Misspelling => "拼写纠正",
        RelationKinds.Synonym => "近义",
        RelationKinds.Antonym => "反义",
        RelationKinds.Derivational => "派生",
        _ => relationType,
    };
}

/// <summary>Control-center display wrapper over <see cref="ConfusableItem"/>.</summary>
public sealed record ConfusableRow(ConfusableItem Item)
{
    public string Spelling => Item.Spelling;

    public string RelationLabel => RelationTypeLabels.Describe(Item.RelationType);

    public string Phonetic => Item.Phonetic;

    public string Chinese => Item.Chinese;

    public string Contrast => Item.Contrast;
}
