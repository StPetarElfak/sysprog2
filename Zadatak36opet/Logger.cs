namespace Zadatak36
{
    public static class Logger
    {
        private static object brava = new object();
        public static void Log(string poruka)
        {
            lock (brava)
            {
                Console.WriteLine($"Nit broj {Thread.CurrentThread.ManagedThreadId} kaze:" + $"{poruka}");
            }
        }
    }
}