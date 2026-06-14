namespace Zadatak36
{
    internal class CacheUnos
    {
        public byte[] Data { get; } // podaci
        public string ContentType { get; } // tip odgovora servera
        public DateTime LastUsed { get; set; }
        public CacheUnos(byte[] data, string contentType)
        {
            Data = data;
            ContentType = contentType;
            LastUsed = DateTime.Now;
        }
    }
}
