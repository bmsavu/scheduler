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
    public HashSet<string> OccupiedToday { get; set; } = [];

    // Add form bind properties
    [BindProperty]
    public DateOnly Data { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    [BindProperty]
    public int Hour { get; set; } = 9;

    [BindProperty]
    public int Minute { get; set; } = 0;

    [BindProperty]
    public int Repeat { get; set; } = 1;

    [BindProperty]
    public string Destinatie { get; set; } = "ET1L01";

    public string LastDestinatie { get; set; } = "ET1L01";

    public IndexModel(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Scheduler")!;
    }

    public void OnGet()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ro-RO");
        LoadUserInfo();
        SetViewData();
        LoadLastDestination();
        LoadReservations();
    }

    public IActionResult OnGetOccupied(string date)
    {
        var occupied = new Dictionary<string, string>();
        using var con = new SqlConnection(_connectionString);
        con.Open();
        using var cmd = new SqlCommand("SELECT destinatie, numecomplet FROM Orar WHERE data = @date", con);
        cmd.Parameters.AddWithValue("@date", date);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            occupied[reader.GetString(0)] = reader.GetString(1);
        return new JsonResult(occupied);
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
        var rows = cmd.ExecuteNonQuery();

        if (rows > 0)
            TempData["SuccessMessage"] = "Rezervarea a fost stearsa.";

        return RedirectToPage();
    }

    public IActionResult OnPostAdd()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ro-RO");
        LoadUserInfo();

        if (Repeat > 5) Repeat = 5;
        if (Repeat < 1) Repeat = 1;

        var strDest = string.IsNullOrEmpty(Destinatie) ? "Neprecizat" : Destinatie;
        var dtOra = Data.ToDateTime(new TimeOnly(Hour, Minute));

        if (dtOra < DateTime.Now)
            dtOra = DateTime.Now.AddMinutes(5);

        using var con = new SqlConnection(_connectionString);
        con.Open();

        for (int i = 0; i < Repeat; i++)
        {
            var dateStr = dtOra.ToString("yyyy-MM-dd");
            using var cmd = new SqlCommand(
                @"IF NOT EXISTS (SELECT 1 FROM Orar WHERE Data = @date AND destinatie = @dest)
                  AND NOT EXISTS (SELECT 1 FROM Orar WHERE Data = @date AND nume = @nume)
                  BEGIN
                      INSERT INTO Orar (nume, numecomplet, ora, destinatie, rol)
                      VALUES (@nume, @fullname, @ora, @dest, @rol)
                  END", con);

            cmd.Parameters.AddWithValue("@date", dateStr);
            cmd.Parameters.AddWithValue("@dest", strDest);
            cmd.Parameters.AddWithValue("@nume", ShortName);
            cmd.Parameters.AddWithValue("@fullname", FullName);
            cmd.Parameters.AddWithValue("@ora", dtOra.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@rol", "Rezervare");
            cmd.ExecuteNonQuery();

            dtOra = dtOra.AddDays(1);
        }

        TempData["SuccessMessage"] = "Rezervarea a fost adaugata cu succes.";
        return RedirectToPage();
    }

    private void SetViewData()
    {
        ViewData["FullName"] = FullName;
        ViewData["UserName"] = UserName;
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

    private void LoadLastDestination()
    {
        using var con = new SqlConnection(_connectionString);
        con.Open();
        using var cmd = new SqlCommand(
            "SELECT TOP 1 destinatie FROM Orar WHERE nume = @nume ORDER BY ora DESC", con);
        cmd.Parameters.AddWithValue("@nume", ShortName);

        var result = cmd.ExecuteScalar();
        if (result != null)
            LastDestinatie = result.ToString()!;
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

        var today = DateTime.Today;
        while (reader.Read())
        {
            var vm = new RezervareViewModel
            {
                OrarId = reader.GetInt32(reader.GetOrdinal("orarId")),
                Ora = reader.GetDateTime(reader.GetOrdinal("ora")),
                NumeComplet = reader.GetString(reader.GetOrdinal("numecomplet")),
                Destinatie = reader.GetString(reader.GetOrdinal("destinatie")),
                IsOwner = reader.GetString(reader.GetOrdinal("nume")) == ShortName
            };
            Rezervari.Add(vm);

            if (vm.Ora.Date == today)
                OccupiedToday.Add(vm.Destinatie);
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
