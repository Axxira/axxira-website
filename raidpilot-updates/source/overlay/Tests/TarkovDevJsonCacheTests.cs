using RaidPilot.Overlay;

namespace RaidPilot.Overlay.Tests;

internal static class TarkovDevJsonCacheTests
{
    internal const int TestCount = 6;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("cache tarkov.dev enregistré après succès", SavesValidatedNetworkResponse, failures);
        Run("cache tarkov.dev utilisé hors ligne", FallsBackToLastValidResponse, failures);
        Run("cache tarkov.dev corrompu refusé", RejectsCorruptCachedResponse, failures);
        Run("annulation du cache tarkov.dev respectée", PreservesCallerCancellation, failures);
        Run("catalogue livré utilisable sans réseau et modes isolés", BundledCatalogueAvoidsNetwork, failures);
        Run("catalogue livré corrompu utilise le repli", CorruptBundleFallsBack, failures);
    }

    private static void SavesValidatedNetworkResponse()
    {
        WithTemporaryCache((cache, root) =>
        {
            const string payload = "{\"data\":{\"maps\":[]}}";
            var result = cache.GetOrFetchAsync(
                    "regular-maps",
                    1024,
                    "maps",
                    _ => Task.FromResult(payload),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Require(result == payload, "la réponse réseau a été modifiée");
            Require(File.ReadAllText(Path.Combine(root, "regular-maps.json")) == payload,
                "la dernière réponse valide n'a pas été enregistrée");
        });
    }

    private static void FallsBackToLastValidResponse()
    {
        WithTemporaryCache((cache, _) =>
        {
            const string payload = "{\"data\":{\"tasks\":[]}}";
            _ = cache.GetOrFetchAsync(
                    "regular-tasks",
                    1024,
                    "tasks",
                    _ => Task.FromResult(payload),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var result = cache.GetOrFetchAsync(
                    "regular-tasks",
                    1024,
                    "tasks",
                    _ => Task.FromException<string>(new HttpRequestException("hors ligne")),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Require(result == payload, "le cache valide n'a pas pris le relais");
        });
    }

    private static void RejectsCorruptCachedResponse()
    {
        WithTemporaryCache((cache, root) =>
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "regular-maps.json"), "{cassé");
            try
            {
                _ = cache.GetOrFetchAsync(
                        "regular-maps",
                        1024,
                        "maps",
                        _ => Task.FromException<string>(new HttpRequestException("hors ligne")),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                throw new InvalidOperationException("un cache corrompu a été accepté");
            }
            catch (HttpRequestException)
            {
            }
        });
    }

    private static void PreservesCallerCancellation()
    {
        WithTemporaryCache((cache, _) =>
        {
            const string payload = "{\"data\":{\"maps\":[]}}";
            _ = cache.GetOrFetchAsync(
                    "regular-maps",
                    1024,
                    "maps",
                    _ => Task.FromResult(payload),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                _ = cache.GetOrFetchAsync(
                        "regular-maps",
                        1024,
                        "maps",
                        _ => Task.FromException<string>(new OperationCanceledException(cancellation.Token)),
                        cancellation.Token)
                    .GetAwaiter()
                    .GetResult();
                throw new InvalidOperationException("l'annulation appelant a été masquée par le cache");
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private static void BundledCatalogueAvoidsNetwork()
    {
        WithTemporaryCache((_, root) => {
            var bundle = Path.Combine(root, "bundle"); Directory.CreateDirectory(Path.Combine(bundle,"pve"));
            const string regular = "{\"data\":{\"tasks\":{\"regular\":{}}}}";
            const string pve = "{\"data\":{\"tasks\":{\"pve\":{}}}}";
            File.WriteAllText(Path.Combine(bundle,"tasks.json"),regular);
            File.WriteAllText(Path.Combine(bundle,"pve","tasks.json"),pve);
            var cache = new TarkovDevJsonCache(root,bundle); var calls=0;
            Task<string> Network(CancellationToken _) { calls++; return Task.FromResult("{\"data\":{\"tasks\":{}}}"); }
            var a=cache.GetOrFetchAsync("regular-tasks",1024,"tasks",Network,CancellationToken.None).GetAwaiter().GetResult();
            var b=cache.GetOrFetchAsync("pve-tasks",1024,"tasks",Network,CancellationToken.None).GetAwaiter().GetResult();
            Require(a==regular && b==pve && calls==0,"le catalogue doit être local et propre au mode");
            cache.GetOrFetchAsync("pvp-season-tasks",1024,"tasks",Network,CancellationToken.None).GetAwaiter().GetResult();
            Require(calls==1,"le mode saison ne doit pas emprunter les tâches regular");
        });
    }
    private static void CorruptBundleFallsBack()
    {
        WithTemporaryCache((_, root) => {
            var bundle=Path.Combine(root,"bundle"); Directory.CreateDirectory(bundle);
            File.WriteAllText(Path.Combine(bundle,"tasks.json"),"{bad}");
            const string payload="{\"data\":{\"tasks\":{}}}";
            var cache=new TarkovDevJsonCache(root,bundle);
            var result=cache.GetOrFetchAsync("regular-tasks",1024,"tasks",_=>Task.FromResult(payload),CancellationToken.None).GetAwaiter().GetResult();
            Require(result==payload,"catalogue invalide non rejeté");
        });
    }
    private static void WithTemporaryCache(Action<TarkovDevJsonCache, string> action)
    {
        // Le bac à sable de tests permet l'écriture et le remplacement
        // atomique dans le dossier de sortie du projet.
        var root = Path.Combine(AppContext.BaseDirectory, $"RaidPilot-cache-tests-{Guid.NewGuid():N}");
        try
        {
            action(new TarkovDevJsonCache(root), root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void Run(string name, Action test, ICollection<string> failures)
    {
        try
        {
            test();
            Console.WriteLine($"OK  {name}");
        }
        catch (Exception exception)
        {
            failures.Add($"{name} — {exception.Message}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
