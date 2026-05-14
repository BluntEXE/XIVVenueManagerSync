using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VenueManager
{
  public class XIVAppVenue
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("addresses")]
    public List<string> Addresses { get; set; } = new();

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";
  }

  public class Service
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price")]
    public string Price { get; set; } = "";

    [JsonPropertyName("category")]
    public string? Category { get; set; }
  }

  public class ServicesResponse
  {
    [JsonPropertyName("services")]
    public List<Service> Services { get; set; } = new();

    [JsonPropertyName("userRole")]
    public string? UserRole { get; set; }
  }

  public class Role
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
  }

  public class RolesResponse
  {
    [JsonPropertyName("roles")]
    public List<Role> Roles { get; set; } = new();
  }

  public class XIVAppVenuesResponse
  {
    [JsonPropertyName("venues")]
    public List<XIVAppVenue> Venues { get; set; } = new();
  }

  public class ActiveEventResponse
  {
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("eventId")]
    public string? EventId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
  }

  public class XIVAppPatronVisitRequest
  {
    [JsonPropertyName("venueId")]
    public string VenueId { get; set; } = "";

    [JsonPropertyName("characterName")]
    public string CharacterName { get; set; } = "";

    [JsonPropertyName("world")]
    public string World { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";
  }

  public class XIVAppServiceRequest
  {
    [JsonPropertyName("venueId")]
    public string VenueId { get; set; } = "";

    [JsonPropertyName("guestName")]
    public string GuestName { get; set; } = "";

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
  }

  public class XIVAppTransactionRequest
  {
    [JsonPropertyName("venueId")]
    public string VenueId { get; set; } = "";

    // Server uses zod .optional() which accepts string|undefined but NOT
    // null. System.Text.Json default-serializes null as literal `null`,
    // which zod rejects ("Expected string, received null"). Omit the
    // property entirely when null so the server sees it as undefined.
    [JsonPropertyName("serviceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServiceId { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("customerName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomerName { get; set; }

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }
  }

  public class LogTransactionResult
  {
    public bool Success { get; set; }
    public string? Error { get; set; }
  }

  public class ShiftDto
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("scheduledStart")]
    public string ScheduledStart { get; set; } = "";

    [JsonPropertyName("scheduledEnd")]
    public string ScheduledEnd { get; set; } = "";

    [JsonPropertyName("actualStart")]
    public string? ActualStart { get; set; }

    [JsonPropertyName("actualEnd")]
    public string? ActualEnd { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
  }

  public class ShiftsResponse
  {
    [JsonPropertyName("shifts")]
    public List<ShiftDto> Shifts { get; set; } = new();
  }

  public class ClockResult
  {
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Status { get; set; }
    public double? HoursWorked { get; set; }
  }

  public class XIVAppApiException : Exception
  {
    public XIVAppApiException(string message) : base(message) { }
    public XIVAppApiException(string message, Exception inner) : base(message, inner) { }
  }
}
