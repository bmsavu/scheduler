using System.DirectoryServices.AccountManagement;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace Scheduler.Pages;

public class IndexModel : PageModel
{
    private readonly string _connectionString;

    public string UserName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public List<RezervareViewModel> Rezervari { get; set; } = [];

    public IndexModel(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Scheduler")!;
    }

    public void OnGet()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ro-RO");
        LoadUserInfo();
        LoadReservations();
    }

    public IActionResult OnPostDelete(int id)
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ro-RO");
        LoadUserInfo();

        using var con = new SqlConnection(_connectionString);
        con.Open();
        using var cmd = new SqlCommand("DELETE FROM Orar WHERE orarId = @id AND nume = @nume", con);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@nume", ShortName);
        cmd.ExecuteNonQuery();

        return RedirectToPage();
    }

    private void LoadUserInfo()
    {
        UserName = User.Identity?.Name ?? "";
        ShortName = UserName.Contains('\\') ? UserName[(UserName.IndexOf('\\') + 1)..] : UserName;

        try
        {
            using var ctx = new PrincipalContext(ContextType.Domain);
            var userPrincipal = UserPrincipal.FindByIdentity(ctx, UserName);
            FullName = userPrincipal?.DisplayName ?? ShortName;
        }
        catch
        {
            FullName = ShortName;
        }
    }

    private void LoadReservations()
    {
        using var con = new SqlConnection(_connectionString);
        con.Open();

        // Delete old reservations
        using (var cmdDelete = new SqlCommand("DELETE FROM Orar WHERE ora < CAST(GETDATE() AS date)", con))
        {
            cmdDelete.ExecuteNonQuery();
        }

        // Load current reservations
        using var cmd = new SqlCommand(
            "SELECT orarId, ora, numecomplet, destinatie, nume FROM Orar WHERE data >= CAST(GETDATE() AS date) ORDER BY data, destinatie",
            con);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            Rezervari.Add(new RezervareViewModel
            {
                OrarId = reader.GetInt32(reader.GetOrdinal("orarId")),
                Ora = reader.GetDateTime(reader.GetOrdinal("ora")),
                NumeComplet = reader.GetString(reader.GetOrdinal("numecomplet")),
                Destinatie = reader.GetString(reader.GetOrdinal("destinatie")),
                IsOwner = reader.GetString(reader.GetOrdinal("nume")) == ShortName
            });
        }
    }
}

public class RezervareViewModel
{
    public int OrarId { get; set; }
    public DateTime Ora { get; set; }
    public string NumeComplet { get; set; } = "";
    public string Destinatie { get; set; } = "";
    public bool IsOwner { get; set; }
}
