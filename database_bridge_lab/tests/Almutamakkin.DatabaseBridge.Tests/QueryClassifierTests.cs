using Almutamakkin.DatabaseBridge.Core;



namespace Almutamakkin.DatabaseBridge.Tests;



public sealed class QueryClassifierTests

{

    private readonly QueryClassifier _classifier = new();



    [Theory]

    [InlineData("SELECT * FROM dbo.Items", QueryClassification.Read)]

    [InlineData("  WITH cte AS (SELECT 1 AS n) SELECT * FROM cte", QueryClassification.Read)]

    [InlineData("SET NOCOUNT ON; SELECT 1", QueryClassification.Read)]

    [InlineData("DECLARE @x INT = 1; SELECT @x", QueryClassification.Read)]

    [InlineData("EXEC dbo.GetReport", QueryClassification.Read)]

    [InlineData("INSERT INTO dbo.Items VALUES (1)", QueryClassification.Write)]

    [InlineData("UPDATE dbo.Items SET Qty = 1", QueryClassification.Write)]

    [InlineData("DELETE FROM dbo.Items WHERE Id = 1", QueryClassification.Write)]

    [InlineData("SET NOCOUNT ON; DELETE FROM dbo.Items", QueryClassification.Write)]

    [InlineData("SELECT Col INTO dbo.Temp FROM dbo.Items", QueryClassification.Write)]

    [InlineData("CREATE TABLE dbo.Test (Id INT)", QueryClassification.Schema)]

    [InlineData("BACKUP DATABASE X TO DISK = 'x.bak'", QueryClassification.Administrative)]

    [InlineData("CREATE TABLE #T(Id INT); INSERT INTO #T VALUES (1); SELECT * FROM #T;", QueryClassification.Read)]

    [InlineData("SELECT Col INTO #T FROM dbo.Items", QueryClassification.Read)]

    [InlineData("DROP TABLE #T", QueryClassification.Read)]

    [InlineData("INSERT INTO @t VALUES (1)", QueryClassification.Read)]

    public void Classify_ReturnsExpectedClassification(string sql, QueryClassification expected)

    {

        var result = _classifier.Classify(sql);

        Assert.Equal(expected, result);

    }



    [Fact]

    public void Classify_EmptySql_ReturnsUnknown()

    {

        Assert.Equal(QueryClassification.Unknown, _classifier.Classify(string.Empty));

    }



    [Fact]

    public void ContainsForbiddenDataChange_IgnoresInsertInsideStringLiteral()

    {

        Assert.False(

            QueryClassifier.ContainsForbiddenDataChange(

                "SELECT 'INSERT INTO fake' AS Note"));

    }



    [Fact]

    public void ContainsForbiddenDataChange_AllowsTempInsert_BlocksPermanentInsert()

    {

        Assert.False(

            QueryClassifier.ContainsForbiddenDataChange(

                "INSERT INTO #PIG(ProductID) SELECT 1"));

        Assert.True(

            QueryClassifier.ContainsForbiddenDataChange(

                "INSERT INTO dbo.Items(Id) VALUES (1)"));

    }



    [Fact]

    public void ContainsPermanentSchemaChange_AllowsTempCreate_BlocksPermanentCreate()

    {

        Assert.False(

            QueryClassifier.ContainsPermanentSchemaChange(

                "IF OBJECT_ID('tempdb..#PIG') IS NOT NULL DROP TABLE #PIG; CREATE TABLE #PIG(Id INT);"));

        Assert.True(

            QueryClassifier.ContainsPermanentSchemaChange(

                "SET NOCOUNT ON; CREATE TABLE dbo.X(Id INT);"));

    }



    [Theory]

    [InlineData("#PIG", true)]

    [InlineData("##Global", true)]

    [InlineData("@tv", true)]

    [InlineData("tempdb..#PIG", true)]

    [InlineData("[#PIG]", true)]

    [InlineData("dbo.Items", false)]

    [InlineData("Inventory.Data_Products", false)]

    public void IsSessionLocalObject_DetectsTempTargets(string name, bool expected)

    {

        Assert.Equal(expected, QueryClassifier.IsSessionLocalObject(name));

    }

}


