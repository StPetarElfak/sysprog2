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
        private int brojNiti;
        private object bravaBrojNiti;
        private static readonly ConcurrentDictionary<string, CacheUnos> cache = new();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> semafori = new();
        private static int maxUnosa = 10;
        private static List<string> podrzaniTipovi = new List<string> { ".jpg", ".jpeg", ".gif", ".bmp", ".exif", ".png", ".tiff" };
        public Server(int port, int maxNiti)
        {
            this.port = port;
            listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            radi = false;
            ThreadPool.SetMaxThreads(maxNiti, maxNiti);
            brojNiti = 0;
            bravaBrojNiti = new object();
        }
        public async void Pokreni()
        {
            radi = true;
            listener.Start();
            Logger.Log("Server pokrenut.");
            while (radi)
            {
                try
                {
                    HttpListenerContext context = listener.GetContext();
                    Logger.Log("Primljen novi zahtev");
                    Task.Run(() => ObradaZahteva(context));
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
        private async Task ObradaZahteva(object request)
        {
            var context = (HttpListenerContext)request;
            var vremeObrade = Stopwatch.StartNew();
            try
            {
                string naziv = context.Request.Url.AbsolutePath.TrimStart('/');
                string ex = Path.GetExtension(naziv);
                if (ex == ".ico")
                {
                    context.Response.StatusCode = 204;
                    Logger.Log("favicon.ico request, poslat prazan odgovor");
                }
                else if (podrzaniTipovi.Contains(ex))
                {
                    string? pragString = HttpUtility.ParseQueryString(context.Request.Url.Query).Get("prag");
                    if (pragString == null) throw (new Exception("Prag nije naznacen"));
                    byte prag = Byte.Parse(pragString);
                    string key = naziv + pragString;
                    Logger.Log($"Zahtev za binarizacijom: {naziv}, prag={pragString}");
                    CacheUnos? unos;
                    if (cache.TryGetValue(key, out unos))
                    {
                        Logger.Log($"Nadjen u kesu: {key}");
                        await PostaviIzKesa(naziv, prag, context, unos);
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
                                await PostaviIzKesa(naziv, prag, context, unos);
                            }
                            else await BinarizujIPostavi(naziv, prag, context);
                        }
                        finally
                        {
                            fileLock.Release();
                        }
                    }
                }
                else throw (new NotSupportedException("Tip fajla nije podrzan"));
            }

            catch (FileNotFoundException e)
            {
                Logger.Log($"Fajl ne postoji: {e.Message}");
                await PosaljiGresku(context, e.Message, HttpStatusCode.NotFound);
            }
            catch (NotSupportedException e)
            {
                Logger.Log($"fajl nije podrzan: {e.Message}");
                await PosaljiGresku(context, e.Message, HttpStatusCode.NotFound);
            }
            catch (Exception e)
            {
                Logger.Log($"Neka greska: {e.Message}");
                await PosaljiGresku(context, e.Message, HttpStatusCode.InternalServerError);
            }
            finally
            {
                vremeObrade.Stop();
                Logger.Log($"Vreme obrade zahteva {context.Request.Url}: {vremeObrade.ElapsedMilliseconds}ms\n");
                lock (bravaBrojNiti) brojNiti--;
                context.Response.Close();
            }
        }
        private static async Task PostaviIzKesa(string naziv, byte prag, HttpListenerContext context, CacheUnos cacheUnos)
        {
            cache[naziv+prag].LastUsed = DateTime.Now;
            context.Response.ContentType = cacheUnos.ContentType;
            context.Response.ContentLength64 = cacheUnos.Data.Length;
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            using (var output = context.Response.OutputStream)
            {
                await output.WriteAsync(cacheUnos.Data, 0, cacheUnos.Data.Length);
            }
            Logger.Log($"Fajl postavljen iz kesa: {naziv}");
        }
        private async Task BinarizujIPostavi(string naziv, byte prag, HttpListenerContext context)
        {
            byte[] data;
            Logger.Log($"Nije nadjen u kesu, vrsi se binarizacija: {naziv}, prag={prag}");
            var b = Binarizator.Binarizuj(naziv, prag);
            await b.ContinueWith(async antecedent =>
            {
                if (antecedent.Status == TaskStatus.RanToCompletion)
                {
                    byte[] data = antecedent.Result;
                    string extension = Path.GetExtension(naziv);
                    string contentType = "image/" + extension.TrimStart('.');
                    CacheUnos zaUneti = new CacheUnos(data, contentType);
                    UnesiUCache(naziv + prag, zaUneti);
                    context.Response.ContentType = contentType;
                    context.Response.ContentLength64 = data.Length;
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    using (var output = context.Response.OutputStream)
                    {
                        await output.WriteAsync(data, 0, data.Length);
                    }
                    Logger.Log($"Fajl binarizovan: {naziv}");
                }
            });
            
        }
        private async Task PosaljiGresku(HttpListenerContext context, string poruka, HttpStatusCode code)
        {
            try
            {
                byte[] porukaUBajtovima = Encoding.UTF8.GetBytes(poruka);
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = porukaUBajtovima.Length;
                context.Response.StatusCode = (int)code;
                using (Stream output = context.Response.OutputStream)
                {
                    await output.WriteAsync(porukaUBajtovima, 0, porukaUBajtovima.Length);
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
