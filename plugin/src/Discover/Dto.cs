using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.CinemaGlass.Discover;

/// <summary>Die Antworten an das Theme. Feldnamen und Bedeutungen sind mit dem Theme vereinbart; nichts umbenennen.</summary>
internal static class DiscoverJson
{
    /// <summary>camelCase, leere Felder als <c>null</c> (das Theme rechnet mit jedem Feld), Umlaute ungeschützt.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions Input = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 8,
    };
}

internal sealed class ItemDto
{
    public string Key { get; set; } = string.Empty;

    public string MediaType { get; set; } = "movie";

    public int TmdbId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public string? Overview { get; set; }

    public string? Poster { get; set; }

    public string? Backdrop { get; set; }

    public string[] Genres { get; set; } = Array.Empty<string>();

    public double? Rating { get; set; }

    public int Match { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string ReasonKind { get; set; } = "genre";

    public string State { get; set; } = "missing";

    public string? ItemId { get; set; }

    public int? Runtime { get; set; }

    public int? Seasons { get; set; }

    public string? ReleaseDate { get; set; }

    /// <summary>Nur bei angefragten Titeln: wie weit die Anfrage ist.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RequestDto? Request { get; set; }
}

/// <summary>Stand einer Anfrage („Unterwegs zu dir“).</summary>
internal sealed class RequestDto
{
    public string Phase { get; set; } = "searching";

    public int? Percent { get; set; }

    public int? EtaMinutes { get; set; }

    public string? Eta { get; set; }

    public string Text { get; set; } = string.Empty;

    public string Short { get; set; } = string.Empty;

    public string? RequestedAt { get; set; }

    public string? AvailableAt { get; set; }

    /// <summary>Ab wann mit dem Titel zu rechnen ist (JJJJ-MM-TT), wenn es dafür einen Termin gibt.</summary>
    public string? AvailableFrom { get; set; }

    public bool IsNew { get; set; }

    public string? Seasons { get; set; }
}

internal sealed class RequestsDto
{
    public string Status { get; set; } = "ready";

    public int Active { get; set; }

    public int Poll { get; set; }

    public List<ItemDto> Items { get; set; } = new();
}

internal sealed class HomeRequestsDto
{
    public string Title { get; set; } = string.Empty;

    public int Active { get; set; }

    public int Poll { get; set; }

    public List<ItemDto> Items { get; set; } = new();
}

internal sealed class HomeArrivedDto
{
    public string Title { get; set; } = string.Empty;

    public List<ItemDto> Items { get; set; } = new();
}

internal sealed class ChipDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string MediaType { get; set; } = "movie";

    public double Weight { get; set; }
}

internal sealed class ChipsDto
{
    public List<ChipDto> Genres { get; set; } = new();
}

internal sealed class RowDto
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = "poster";

    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    public List<ItemDto> Items { get; set; } = new();
}

internal sealed class PageDto
{
    public string Status { get; set; } = "ready";

    public double Progress { get; set; } = 1.0;

    public string? Error { get; set; }

    public string? GeneratedAt { get; set; }

    public double Confidence { get; set; }

    public int Signals { get; set; }

    public bool NeedsTaste { get; set; }

    public ChipsDto Chips { get; set; } = new();

    public List<ItemDto> Hero { get; set; } = new();

    public List<RowDto> Rows { get; set; } = new();
}

internal sealed class HomeDto
{
    public string Status { get; set; } = "ready";

    public string Title { get; set; } = string.Empty;

    public List<ItemDto> Items { get; set; } = new();

    /// <summary>Angefragtes, das noch unterwegs ist; hängt nicht an der Berechnung der Empfehlungen.</summary>
    public HomeRequestsDto Requests { get; set; } = new();

    /// <summary>Angekommenes, das das Konto noch nicht gesehen hat.</summary>
    public HomeArrivedDto Arrived { get; set; } = new();
}

internal sealed class SimilarDto
{
    public string Status { get; set; } = "ready";

    public List<ItemDto> Items { get; set; } = new();
}

internal sealed class TasteGroupDto
{
    public string Feature { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> Posters { get; set; } = new();

    public bool Selected { get; set; }
}

internal sealed class TasteDto
{
    public bool Done { get; set; }

    public List<TasteGroupDto> Groups { get; set; } = new();

    public List<ItemDto> Items { get; set; } = new();
}

internal sealed class FeatureDto
{
    public string Feature { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    public double Weight { get; set; }

    public bool Muted { get; set; }
}

internal sealed class FeedbackListsDto
{
    public List<ItemDto> Liked { get; set; } = new();

    public List<ItemDto> Disliked { get; set; } = new();

    public List<ItemDto> Hidden { get; set; } = new();
}

internal sealed class ProfileDto
{
    public double Confidence { get; set; }

    public int Signals { get; set; }

    public List<FeatureDto> Genres { get; set; } = new();

    public List<FeatureDto> People { get; set; } = new();

    public List<FeatureDto> Decades { get; set; } = new();

    public List<FeatureDto> Keywords { get; set; } = new();

    public FeedbackListsDto Feedback { get; set; } = new();
}

internal sealed class FeedbackResultDto
{
    public bool Ok { get; set; }

    public int Signals { get; set; }

    public double Confidence { get; set; }
}

internal sealed class OkDto
{
    public bool Ok { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}

// ---- Eingaben ----------------------------------------------------------------------------------------------------

internal sealed class FeedbackInput
{
    public string? Source { get; set; }

    public List<FeedbackItemInput>? Items { get; set; }
}

internal sealed class FeedbackItemInput
{
    public string? Key { get; set; }

    public string? Action { get; set; }
}

internal sealed class GroupsInput
{
    public List<string>? Features { get; set; }
}

internal sealed class MuteInput
{
    public string? Feature { get; set; }

    public bool Muted { get; set; }
}

internal sealed class ImpressionsInput
{
    public List<string>? Keys { get; set; }
}
