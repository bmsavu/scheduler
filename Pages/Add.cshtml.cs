using System.DirectoryServices.AccountManagement;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace Scheduler.Pages;

public class AddModel : PageModel
{
    private readonly string _connectionString;

    public string UserName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string ShortName { get; set; } = "";

    [BindProperty]
    public string Rol { get; set; } = "Rezervare";

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

    public AddModel(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Scheduler")!;
    }

    public void OnGet()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ro-RO");
        LoadUserInfo();
        LoadLastDestination();
    }

    public IActionResult OnPost()
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
            cmd.Parameters.AddWithValue("@rol", Rol);
            cmd.ExecuteNonQuery();

            dtOra = dtOra.AddDays(1);
        }

        return RedirectToPage("/Index");
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
            Destinatie = result.ToString()!;
    }
}
