using MediCare.Web.Models;

namespace MediCare.Web.Services;

public static class LaboratoryRules
{
    public static bool MayCollect(LabRequest r)=>r.Status==LabRequestStatus.Requested;
    public static bool MayReceive(LabRequest r)=>r.Status==LabRequestStatus.SampleCollected && r.ReceivedAtUtc==null;
    public static bool MayProcess(LabRequest r)=>r.Status==LabRequestStatus.SampleCollected && r.ReceivedAtUtc!=null;
    public static bool MayCancel(LabRequest r)=>r.Status is LabRequestStatus.Requested or LabRequestStatus.SampleCollected or LabRequestStatus.InProgress;
    public static string? ValidateResult(LabResultKind kind,decimal? numeric,string? text,bool finalizing)
    {
        if(!Enum.IsDefined(kind))return "Unknown result format.";
        if(kind==LabResultKind.Numeric)
        {
            if(!string.IsNullOrWhiteSpace(text))return "This test requires a numeric result, not text.";
            if(numeric.HasValue && (numeric.Value < -99999999999999999999m || numeric.Value > 99999999999999999999m || decimal.Round(numeric.Value,8)!=numeric.Value))return "Numeric results support at most 20 integer digits and 8 decimal places.";
            if(finalizing && !numeric.HasValue)return "A numeric result is required before finalization.";
        }
        else
        {
            if(numeric.HasValue)return "This test requires a text result, not a number.";
            if(finalizing && string.IsNullOrWhiteSpace(text))return "A text result is required before finalization.";
        }
        return null;
    }
}
