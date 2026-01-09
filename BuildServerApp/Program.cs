namespace BuildServerApp;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
        catch (Exception ex)
        {
            File.WriteAllText("startup_error.txt", ex.ToString());
            MessageBox.Show(ex.Message, "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }    
}