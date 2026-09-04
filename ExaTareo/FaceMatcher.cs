namespace ExaTareo;

public sealed record FaceIdentificationResult(
    bool IsMatch,
    bool IsAmbiguous,
    RegisteredPerson? Person,
    double Score,
    double? RunnerUpScore);

public static class FaceMatcher
{
    // SFace reference cosine threshold. Demo only; requires local accuracy evaluation.
    public const double Threshold = 0.363;
    public const double IdentificationMargin = 0.03;

    public static double Compare(float[] enrolled, float[] captured)
    {
        var a = FaceEngine.Normalize(enrolled);
        try
        {
            var b = FaceEngine.Normalize(captured);
            try { return Math.Clamp(a.Zip(b).Sum(p => (double)p.First * p.Second), -1, 1); }
            finally { Array.Clear(b); }
        }
        finally { Array.Clear(a); }
    }

    public static FaceIdentificationResult Identify(IReadOnlyList<FaceTemplate> templates, float[] candidate)
    {
        if (templates.Count == 0)
            return new FaceIdentificationResult(false, false, null, 0, null);
        var scores = templates
            .Select(template => new
            {
                Template = template,
                Score = template.Captures.Count == 0
                    ? double.NegativeInfinity
                    : template.Captures.Max(capture => Compare(capture.Vector, candidate))
            })
            .OrderByDescending(match => match.Score)
            .ToList();
        var best = scores[0];
        double? runnerUp = scores.Count > 1 ? scores[1].Score : null;
        bool passesThreshold = best.Score >= Threshold;
        bool ambiguous = passesThreshold && runnerUp.HasValue && best.Score - runnerUp.Value < IdentificationMargin;
        if (!passesThreshold || ambiguous)
            return new FaceIdentificationResult(false, ambiguous, null, Math.Max(0, best.Score), runnerUp);
        return new FaceIdentificationResult(true, false,
            new RegisteredPerson(best.Template.Id, best.Template.NombreUsuario), best.Score, runnerUp);
    }
}
