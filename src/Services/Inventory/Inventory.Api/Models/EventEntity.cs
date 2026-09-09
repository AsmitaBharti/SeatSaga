namespace Inventory.Api.Models;

/// <summary>
/// Minimal event catalog entry. In the full system this would be owned by a
/// separate Catalog service; kept here for the Inventory-Service-only demo slice.
/// </summary>
public class EventEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string VenueName { get; set; } = default!;
    public DateTime StartsAt { get; set; }
    public decimal BasePrice { get; set; }

    public List<Seat> Seats { get; set; } = new();
}
