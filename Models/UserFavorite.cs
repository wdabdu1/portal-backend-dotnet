namespace ShippingPortal.Api.Models;

// One row per menu item a user has pinned to their sidebar Quick Access.
public class UserFavorite
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Route { get; set; } = "";
    public int SortOrder { get; set; }
}
