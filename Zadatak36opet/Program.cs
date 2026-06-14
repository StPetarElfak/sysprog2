namespace Zadatak36
{
    public class Program
    {
        public static void Main()
        {
            Server server = new Server(5050, 10);
            Thread serverThread = new Thread(server.Pokreni);
            serverThread.Start();
            while (Console.ReadKey().Key != ConsoleKey.Escape) { }
            server.Stani();
            serverThread.Join();
        }
    }
}
