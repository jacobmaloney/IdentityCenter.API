namespace DataAccessLibrary.Models;

/// <summary>
/// Record representing a pre-computed ML prediction result stored in the database.
/// </summary>
public class MLPredictionResultRecord
{
    public Guid Id { get; set; }
    public Guid IdentityId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public float PredictedValue { get; set; }
    public bool? PredictedLabel { get; set; }
    public float? Confidence { get; set; }
    public DateTime ScoredAt { get; set; }
}
