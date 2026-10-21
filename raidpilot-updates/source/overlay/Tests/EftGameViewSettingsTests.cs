namespace RaidPilot.Overlay.Tests;

internal static class EftGameViewSettingsTests
{
    internal const int TestCount = 5;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("caméra EFT: lecture FOV", ReadsVerticalFov, failures);
        Run("caméra EFT: repli fichier invalide", FallsBackOnInvalidFile, failures);
        Run("caméra EFT: conversion verticale horizontale", ConvertsVerticalToHorizontal, failures);
        Run("caméra EFT: recadrage réduit le FOV effectif", CropReducesHorizontalFov, failures);
        Run("caméra EFT: paramètres invalides refusés", RejectsInvalidGeometry, failures);
    }

    private static void ReadsVerticalFov()
    {
        InTemp(path =>
        {
            File.WriteAllText(path, "{\"FieldOfView\": 63}");
            var settings = EftGameViewSettings.Read(path);
            RequireNear(settings.VerticalFieldOfViewDegrees, 63, 0.001, "FOV vertical");
        });
    }

    private static void FallsBackOnInvalidFile()
    {
        InTemp(path =>
        {
            File.WriteAllText(path, "{broken");
            var settings = EftGameViewSettings.Read(path);
            RequireNear(
                settings.VerticalFieldOfViewDegrees,
                EftGameViewSettings.DefaultVerticalFieldOfViewDegrees,
                0.001,
                "FOV de repli");
        });
    }

    private static void ConvertsVerticalToHorizontal()
    {
        var horizontal = EftGameViewSettings.CalculateHorizontalFieldOfViewDegrees(50, 16.0 / 9.0);
        Require(horizontal > 78 && horizontal < 80,
            $"50° vertical en 16:9 devrait donner environ 79° horizontal, reçu {horizontal:F2}");
    }

    private static void CropReducesHorizontalFov()
    {
        var full = EftGameViewSettings.CalculateHorizontalFieldOfViewDegrees(50, 16.0 / 9.0);
        var cropped = EftGameViewSettings.CalculateEffectiveHorizontalFieldOfViewDegrees(
            50,
            16.0 / 9.0,
            0.84);
        Require(cropped < full && cropped > 60,
            $"le recadrage doit réduire le FOV sans l'écraser ({full:F2} → {cropped:F2})");
    }

    private static void RejectsInvalidGeometry()
    {
        RequireThrows<ArgumentOutOfRangeException>(
            () => EftGameViewSettings.CalculateHorizontalFieldOfViewDegrees(5, 16.0 / 9.0));
        RequireThrows<ArgumentOutOfRangeException>(
            () => EftGameViewSettings.CalculateHorizontalFieldOfViewDegrees(50, 0));
        RequireThrows<ArgumentOutOfRangeException>(
            () => EftGameViewSettings.CalculateEffectiveHorizontalFieldOfViewDegrees(50, 16.0 / 9.0, 0));
    }

    private static void InTemp(Action<string> action)
    {
        var dir = Path.Combine(Path.GetTempPath(), "RaidPilot-View-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            action(Path.Combine(dir, "Game.ini"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void RequireThrows<TException>(Action action)
        where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"exception {typeof(TException).Name} attendue");
    }

    private static void RequireNear(double actual, double expected, double tolerance, string label)
    {
        if (Math.Abs(actual - expected) > tolerance)
            throw new InvalidOperationException($"{label}: attendu {expected}, reçu {actual}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Run(string name, Action test, ICollection<string> failures)
    {
        try { test(); Console.WriteLine("OK  " + name); }
        catch (Exception ex)
        {
            failures.Add(name + " — " + ex.Message);
            Console.Error.WriteLine("ERR " + name + ": " + ex.Message);
        }
    }
}