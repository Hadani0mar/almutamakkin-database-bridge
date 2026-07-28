using Almutamakkin.DatabaseBridge.Core;

using Almutamakkin.DatabaseBridge.Protocol;



namespace Almutamakkin.DatabaseBridge.Tests;



public sealed class PermissionPolicyTests

{

    private readonly PermissionPolicy _policy = new();

    private readonly QueryClassifier _classifier = new();



    [Fact]

    public void ReadOnly_AllowsSelect_BlocksUpdate()

    {

        var profile = CreateProfile(SqlPermissionLevel.ReadOnly);



        var read = _policy.Evaluate(profile, "SELECT 1", QueryClassification.Read);

        var write = _policy.Evaluate(profile, "UPDATE dbo.Items SET Qty = 1", QueryClassification.Write);



        Assert.True(read.IsAllowed);

        Assert.False(write.IsAllowed);

    }



    [Fact]

    public void ReadOnly_AllowsSetDeclareAndExec_BlocksInsert()

    {

        var profile = CreateProfile(SqlPermissionLevel.ReadOnly);



        var setQuery = _policy.Evaluate(profile, "SET NOCOUNT ON; SELECT 1", QueryClassification.Read);

        var execQuery = _policy.Evaluate(profile, "EXEC dbo.GetReport", QueryClassification.Read);

        var insert = _policy.Evaluate(

            profile,

            "INSERT INTO dbo.Items(Id) VALUES (1)",

            QueryClassification.Write);



        Assert.True(setQuery.IsAllowed);

        Assert.True(execQuery.IsAllowed);

        Assert.False(insert.IsAllowed);

    }



    [Fact]

    public void ReadOnly_AllowsTempTableAnalysisBatch_BlocksPermanentWrite()

    {

        var profile = CreateProfile(SqlPermissionLevel.ReadOnly);

        const string tempBatch = """

            SET NOCOUNT ON;

            IF OBJECT_ID('tempdb..#PIG') IS NOT NULL DROP TABLE #PIG;

            CREATE TABLE #PIG(ProductID INT PRIMARY KEY);

            INSERT INTO #PIG(ProductID) SELECT TOP (1) ProductID_PK FROM Inventory.Data_Products;

            SELECT ProductID FROM #PIG;

            """;



        var tempClassification = _classifier.Classify(tempBatch);

        var tempAllowed = _policy.Evaluate(profile, tempBatch, tempClassification);



        var permanent = "SET NOCOUNT ON; INSERT INTO dbo.Items(Id) VALUES (1); SELECT 1;";

        var permanentClassification = _classifier.Classify(permanent);

        var permanentDenied = _policy.Evaluate(profile, permanent, permanentClassification);



        Assert.Equal(QueryClassification.Read, tempClassification);

        Assert.True(tempAllowed.IsAllowed);

        Assert.Equal(QueryClassification.Write, permanentClassification);

        Assert.False(permanentDenied.IsAllowed);

    }



    [Fact]

    public void ReadOnly_BlocksPermanentCreateTableEvenAfterSet()

    {

        var profile = CreateProfile(SqlPermissionLevel.ReadOnly);

        const string sql = "SET NOCOUNT ON; CREATE TABLE dbo.Forbidden(Id INT); SELECT 1;";



        var classification = _classifier.Classify(sql);

        var result = _policy.Evaluate(profile, sql, classification);



        Assert.Equal(QueryClassification.Schema, classification);

        Assert.False(result.IsAllowed);

    }



    [Fact]

    public void ReadWrite_AllowsUpdate_BlocksSchema()

    {

        var profile = CreateProfile(SqlPermissionLevel.ReadWrite);



        var write = _policy.Evaluate(profile, "UPDATE dbo.Items SET Qty = 1", QueryClassification.Write);

        var schema = _policy.Evaluate(profile, "CREATE TABLE dbo.X(Id INT)", QueryClassification.Schema);



        Assert.True(write.IsAllowed);

        Assert.False(schema.IsAllowed);

    }



    [Fact]

    public void FullAccess_AllowsSchema()

    {

        var profile = CreateProfile(SqlPermissionLevel.FullAccess);



        var schema = _policy.Evaluate(profile, "CREATE TABLE dbo.X(Id INT)", QueryClassification.Schema);



        Assert.True(schema.IsAllowed);

    }



    private static DatabaseProfile CreateProfile(SqlPermissionLevel level) =>

        new()

        {

            Id = Guid.NewGuid(),

            ProfileName = "Test",

            ServerName = ".",

            DatabaseName = "TestDb",

            PermissionLevel = level,

        };

}


