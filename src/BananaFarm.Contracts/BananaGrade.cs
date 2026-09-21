namespace BananaFarm.Contracts;

/// <summary>Grade stamped on a banana at harvest time by the farm.</summary>
public enum BananaGrade
{
    A,
    B,
    C,
    Golden,
}

/// <summary>
/// Grade returned by the classification model. Distinct from <see cref="BananaGrade"/>
/// because the model may answer <c>Other</c>, which the farm never produces, and because
/// the two are deliberately independent sources (see SPEC 6.2).
/// </summary>
public enum ClassifiedGrade
{
    A,
    B,
    C,
    Golden,
    Other,
}
