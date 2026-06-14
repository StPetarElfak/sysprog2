using System.Text;
using System.Net;
using System.Diagnostics;
using System.Xml.Linq;
using System.Web;
using System.Collections.Concurrent;

namespace Zadatak36
{
    internal class Server
    {
        private HttpListener listener;
        private int port;
        private bool radi;
        private int maxNiti;
        private int brojNiti;
        private object bravaBrojNiti;
        private static readonly ConcurrentDictionary<string, CacheUnos> cache = new();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> semafori = new();
        private static int maxUnosa = 10;
        public Server(int port, int maxNiti)
        {
            this.port = port;
            listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            radi = false;
            this.maxNiti = maxNiti;
            brojNiti = 0;
            bravaBrojNiti = new object();
        }
        public void Pokreni()
        {
            radi = true;
            listener.Start();
            Logger.Log("Server pokrenut.");
            while (radi)
            {
                try
                {
                    HttpListenerContext context = listener.GetContext();
                    if (brojNiti <= maxNiti)
                    {
                        lock (bravaBrojNiti)
                        {
                            brojNiti++;
                        }
                        Logger.Log("Primljen novi zahtev");
                        ThreadPool.QueueUserWorkItem(ObradaZahteva, context);
                    }
                    else
                    { 
                        Logger.Log($"Maksimalan broj niti dostignut");
                        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        byte[] poruka = Encoding.UTF8.GetBytes("Maksimalan broj niti dostignut");

                        context.Response.ContentType = "text/plain; charset=utf-8";
                        context.Response.ContentLength64 = poruka.Length;
                        try
                        {
                            using (var output = context.Response.OutputStream)
                            {
                                output.Write(poruka, 0, poruka.Length);
                            }
                        }
                        finally
                        {
                            context.Response.Close();
                        }
                    }
                }
                catch (HttpListenerException) when (!radi)
                {
                    break;
                }
                catch (Exception e)
                {
                    if (radi) Logger.Log($"Greska: {e.Message}");
                }
            }
        }
        private void ObradaZahteva(object request)
        {
            var context = (HttpListenerContext)request;
            var vremeObrade = Stopwatch.StartNew();
            try
            {
                string naziv = context.Request.Url.AbsolutePath.TrimStart('/');
                string pragString = HttpUtility.ParseQueryString(context.Request.Url.Query).Get("prag");
                byte prag = Byte.Parse(pragString);
                string key = naziv + pragString;
                Logger.Log($"Zahtev za binarizacijom: {naziv}, prag={pragString}");
                CacheUnos unos;
                if (cache.TryGetValue(key, out unos))
                {
                    Logger.Log($"Nadjen u kesu: {key}");
                    PostaviIzKesa(naziv, prag, context, unos);
                }
                else
                {
                    var fileLock = semafori.GetOrAdd(key, x => new SemaphoreSlim(1, 1));
                    fileLock.Wait();
                    try
                    {
                        if (cache.TryGetValue(key, out unos))
                        {
                            Logger.Log($"Nadjen u kesu: {key}");
                            PostaviIzKesa(naziv, prag, context, unos);
                        }
                        else BinarizujIPostavi(naziv, prag, context);
                    }
                    finally
                    {
                        fileLock.Release();
                    }
                }
            }

            catch (FileNotFoundException e)
            {
                Logger.Log($"Fajl ne postoji: {e.Message}");
                PosaljiGresku(context, e.Message, HttpStatusCode.NotFound);
            }
            catch (NotSupportedException e)
            {
                Logger.Log($"fajl nije podrzan: {e.Message}");
                PosaljiGresku(context, e.Message, HttpStatusCode.NotFound);
            }
            catch (Exception e)
            {
                Logger.Log($"Neka greska: {e.Message}");
                PosaljiGresku(context, e.Message, HttpStatusCode.InternalServerError);
            }
            finally
            {
                vremeObrade.Stop();
                Logger.Log($"Vreme obrade: {vremeObrade.ElapsedMilliseconds}ms");
                lock (bravaBrojNiti) brojNiti--;
                context.Response.Close();
            }
        }
        private void PostaviIzKesa(string naziv, byte prag, HttpListenerContext context, CacheUnos cacheUnos)
        {
            cache[naziv+prag].LastUsed = DateTime.Now;
            context.Response.ContentType = cacheUnos.ContentType;
            context.Response.ContentLength64 = cacheUnos.Data.Length;
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            using (var output = context.Response.OutputStream)
            {
                output.Write(cacheUnos.Data, 0, cacheUnos.Data.Length);
            }
            Logger.Log($"Fajl postavljen iz kesa: {naziv}");
        }
        private void BinarizujIPostavi(string naziv, byte prag, HttpListenerContext context)
        {
            byte[] data;
            Logger.Log($"Nije nadjen u kesu, vrsi se binarizacija: {naziv}, prag={prag}");
            Binarizator.Binarizuj(naziv, prag, out data);
            string extension = Path.GetExtension(naziv);
            string contentType = "image/" + extension.TrimStart('.');
            CacheUnos zaUneti = new CacheUnos(data, contentType);
            UnesiUCache(naziv + prag, zaUneti);
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = data.Length;
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            using (var output = context.Response.OutputStream)
            {
                output.Write(data, 0, data.Length);
            }
            Logger.Log($"Fajl binarizovan: {naziv}");
        }
        private void PosaljiGresku(HttpListenerContext context, string poruka, HttpStatusCode code)
        {
            try
            {
                byte[] porukaUBajtovima = Encoding.UTF8.GetBytes(poruka);
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = porukaUBajtovima.Length;
                context.Response.StatusCode = (int)code;
                using (Stream output = context.Response.OutputStream)
                {
                    output.Write(porukaUBajtovima, 0, porukaUBajtovima.Length);
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Nije uspelo slanje: {e.Message}");
            }
        }

        public void Stani()
        {
            if (radi)
            {
                radi = false;
                Logger.Log("Zatvaranje");
                int brSec = 0;
                while (brojNiti > 0 && brSec < 5)
                {
                    Thread.Sleep(100);
                }
                listener.Stop();
                listener.Close();
                if (brSec < 5)
                    Logger.Log("Sve niti su stigle da zavrse");
                else
                    Logger.Log("Neke niti nisu zavrsile posao");
            }
        }
        public static void UnesiUCache(string key, CacheUnos unos)
        {
            cache[key] = unos;
            CacheUnos? obrisan;
            if (cache.Count > maxUnosa) cache.Remove(cache.MinBy(x => x.Value.LastUsed).Key, out obrisan);
        }
    }
}
