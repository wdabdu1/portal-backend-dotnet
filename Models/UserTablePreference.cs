namespace ShippingPortal.Api.Models;

// One row per (user, table) — remembers which column they last sorted by
// and in which direction, restored automatically on their next visit.
public class UserTablePreference
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string TableKey { get; set; } = "";
    public string SortColumn { get; set; } = "";
    public bool SortAsc { get; set; } = true;
}
