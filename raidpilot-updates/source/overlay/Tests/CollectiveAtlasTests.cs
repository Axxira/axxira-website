namespace RaidPilot.Overlay.Tests;

internal static class CollectiveAtlasTests
{
    internal const int TestCount = 8;

    internal static void RunAll(
        ICollection<string> failures)
    {
        Run(
            "atlas collectif: identifiant candidat stable",
            CandidateIdIsStable,
            failures);
        Run(
            "atlas collectif: modification falsifiée rejetée",
            TamperedCandidateRejected,
            failures);
        Run(
            "atlas collectif: un contributeur reste en quarantaine",
            OneContributorStaysQuarantined,
            failures);
        Run(
            "atlas collectif: trois contributeurs créent consensus",
            ThreeContributorsCreateConsensus,
            failures);
        Run(
            "atlas collectif: doublons même contributeur insuffisants",
            DuplicateContributorDoesNotCreateConsensus,
            failures);
        Run(
            "atlas collectif: validation locale ne contourne pas consensus",
            LocalValidationDoesNotBypassConsensus,
            failures);
        Run(
            "atlas collectif: hard snap distant refusé",
            CollectiveHardSnapIsRejected,
            failures);
        Run(
            "atlas collectif: session par carte stable",
            RelaySessionIsStablePerMap,
            failures);
    }

    private static void CandidateIdIsStable()
    {
        var a = Candidate(
            "RPA_111111111111111111111111111111111111",
            0);
        var b = Candidate(
            "RPA_111111111111111111111111111111111111",
            0);
        Require(
            a.CandidateId == b.CandidateId,
            "hash collectif instable");
    }

    private static void TamperedCandidateRejected()
    {
        var candidate = Candidate(
            "RPA_222222222222222222222222222222222222",
            0);
        var tampered = candidate with
        {
            X = candidate.X + 50,
        };
        Require(
            !CollectiveAtlasProtocol.TryValidate(
                tampered,
                out _),
            "un candidat modifié après hash ne doit jamais être accepté");
    }

    private static void OneContributorStaysQuarantined()
    {
        var engine = new CollectiveAtlasEngine(
            "RPA_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        engine.TryAdd(
            Candidate(
                "RPA_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                0),
            false,
            out _);
        Require(
            engine.ApprovedCountForMap("customs") == 0,
            "un seul pair ne doit pas devenir collectif");
    }

    private static void ThreeContributorsCreateConsensus()
    {
        var engine = new CollectiveAtlasEngine(
            "RPA_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        engine.TryAdd(
            Candidate(
                "RPA_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                0.0),
            false,
            out _);
        engine.TryAdd(
            Candidate(
                "RPA_CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
                0.4),
            false,
            out _);
        engine.TryAdd(
            Candidate(
                "RPA_DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
                -0.4),
            false,
            out _);

        Require(
            engine.ApprovedCountForMap("customs") >= 1,
            "trois contributeurs cohérents devraient créer un repère collectif");
    }

    private static void DuplicateContributorDoesNotCreateConsensus()
    {
        var engine = new CollectiveAtlasEngine(
            "RPA_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var id =
            "RPA_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        engine.TryAdd(
            Candidate(id, 0.0),
            false,
            out _);
        engine.TryAdd(
            Candidate(id, 0.6),
            false,
            out _);
        engine.TryAdd(
            Candidate(id, -0.6),
            false,
            out _);

        Require(
            engine.ApprovedCountForMap("customs") == 0,
            "un même utilisateur ne doit pas voter trois fois");
    }

    private static void LocalValidationDoesNotBypassConsensus()
    {
        var engine = new CollectiveAtlasEngine(
            "RPA_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        engine.TryAdd(
            Candidate(
                "RPA_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                0),
            false,
            out _);

        Require(
            engine.MarkLocallyValidated(
                "customs",
                new VisualKeyframePose(
                    100,
                    5,
                    200,
                    90)),
            "la validation locale devrait seulement marquer le candidat");
        Require(
            engine.ApprovedCountForMap("customs") == 0,
            "un seul contributeur ne doit jamais contourner le consensus");
    }

    private static void CollectiveHardSnapIsRejected()
    {
        var target =
            new VisualKeyframePose(100, 0, 100, 0);
        var decision =
            new VisualRelocalizationDecision(
                VisualCorrectionMode.HardSnap,
                VisualRelocalizationSource.SuperKeyframe,
                target,
                0.98,
                20,
                0,
                0,
                1,
                1,
                double.PositiveInfinity,
                180,
                true,
                LocalizationHealthState.Green,
                "test");

        Require(
            !OverlayForm
                .ShouldAcceptCollectiveCorrection(
                    decision,
                    usedCollectiveEvidence: true),
            "un atlas distant ne doit jamais provoquer un hard snap seul");
    }

    private static void RelaySessionIsStablePerMap()
    {
        var a =
            CollectiveAtlasRelayClient
                .BuildAtlasSessionId("Customs");
        var b =
            CollectiveAtlasRelayClient
                .BuildAtlasSessionId("customs");
        var c =
            CollectiveAtlasRelayClient
                .BuildAtlasSessionId("woods");

        Require(
            a == b && a != c,
            "session Atlas par carte incorrecte");
    }

    private static CollectiveAtlasCandidate Candidate(
        string contributorId,
        double offset)
    {
        var visual =
            new VisualKeyframeEntrySnapshot
            {
                X = 100 + offset,
                Y = 5,
                Z = 200,
                YawDegrees = 90,
                BitsBase64 =
                    Convert.ToBase64String(
                        new byte[96]),
                ValidBitsBase64 =
                    Convert.ToBase64String(
                        Enumerable.Repeat(
                            (byte)0xFF,
                            96)
                            .ToArray()),
                ValidBitCount = 500,
                TextureStandardDeviation = 20,
            };

        return CollectiveAtlasProtocol.CreateCandidate(
            contributorId,
            "customs",
            new VisualKeyframePose(
                100 + offset,
                5,
                200,
                90),
            visual,
            null,
            1_800_000_000_000,
            "test");
    }

    private static void Run(
        string name,
        Action test,
        ICollection<string> failures)
    {
        try
        {
            test();
            Console.WriteLine($"OK  {name}");
        }
        catch (Exception exception)
        {
            failures.Add(
                $"{name} — {exception.Message}");
        }
    }

    private static void Require(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }
}
