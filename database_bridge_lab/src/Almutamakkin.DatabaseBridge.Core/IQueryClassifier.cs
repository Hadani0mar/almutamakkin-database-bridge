namespace Almutamakkin.DatabaseBridge.Core;

public enum QueryClassification
{
    Read,
    Write,
    Schema,
    Administrative,
    Unknown,
}

public interface IQueryClassifier
{
    QueryClassification Classify(string sql);
}
