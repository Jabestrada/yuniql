﻿using Yuniql.Extensibility;
using System;
using System.IO;
using Yuniql.Core;
using Snowflake.Data.Client;
using System.Collections.Generic;

namespace Yuniql.PlatformTests
{
    public class SnowflakeTestDataService : TestDataServiceBase
    {
        private readonly IDataService _dataService;
        private readonly ITokenReplacementService _tokenReplacementService;

        public SnowflakeTestDataService(IDataService dataService, ITokenReplacementService tokenReplacementService) : base(dataService, tokenReplacementService)
        {
            this._dataService = dataService;
            this._tokenReplacementService = tokenReplacementService;
        }

        public override string GetConnectionString(string databaseName)
        {
            var connectionString = EnvironmentHelper.GetEnvironmentVariable("YUNIQL_TEST_CONNECTION_STRING");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ApplicationException("Missing environment variable YUNIQL_TEST_CONNECTION_STRING. See WIKI for developer guides.");
            }

            var connectionStringBuilder = new SnowflakeDbConnectionStringBuilder();
            connectionStringBuilder.ConnectionString = connectionString;
            connectionStringBuilder.Remove("db");
            connectionStringBuilder.Add("db", databaseName.DoubleQuote());
            return connectionStringBuilder.ConnectionString;
        }

        public override bool CheckIfDbExist(string connectionString)
        {
            var connectionStringBuilder = new SnowflakeDbConnectionStringBuilder();
            connectionStringBuilder.ConnectionString = connectionString;

            //extract the test database name from connection string
            connectionStringBuilder.TryGetValue("db", out object databaseName);

            //prepare the sql statement
            var tokens = new List<KeyValuePair<string, string>> {
             new KeyValuePair<string, string>(RESERVED_TOKENS.YUNIQL_DB_NAME, databaseName.ToString()),
             new KeyValuePair<string, string>(RESERVED_TOKENS.YUNIQL_SCHEMA_NAME, base.SchemaName),
            };
            var sqlStatement = _tokenReplacementService.Replace(tokens, _dataService.GetSqlForCheckIfDatabaseExists());

            //prepare a connection to snowflake without any targetdb or schema, we need to remove these keys else it throws an error that db doesn't exists
            var masterConnectionStringBuilder = new SnowflakeDbConnectionStringBuilder();
            masterConnectionStringBuilder.ConnectionString = connectionString;
            masterConnectionStringBuilder.Remove("db");
            masterConnectionStringBuilder.Remove("schema");

            return base.QueryHasRow(masterConnectionStringBuilder.ConnectionString, sqlStatement);
        }

        public override bool CheckIfDbObjectExist(string connectionString, string objectName)
        {
            var dbObject = objectName.SplitSchema(_dataService.SchemaName);

            var sqlStatement = $"SELECT 1 WHERE EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '{dbObject.Item1}' AND TABLE_NAME = '{dbObject.Item2}' AND TABLE_TYPE = 'BASE TABLE')";
            var result = base.QuerySingleBool(connectionString, sqlStatement);

            if (!result)
            {
                sqlStatement = $"SELECT 1 WHERE EXISTS (SELECT * FROM INFORMATION_SCHEMA.PROCEDURES WHERE PROCEDURE_SCHEMA = '{dbObject.Item1}' AND PROCEDURE_NAME = '{dbObject.Item2}')";
                result = base.QuerySingleBool(connectionString, sqlStatement);
            }

            return result;
        }

        public override string GetSqlForCreateDbSchema(string schemaName)
        {
            return $@"
CREATE SCHEMA {schemaName.DoubleQuote()};
";
        }

        public override string GetSqlForCreateDbObject(string objectName)
        {
            return $@"
CREATE TABLE {objectName.DoubleQuote()}(
    TEST_COLUMN_1 VARCHAR(50) NOT NULL,
    TEST_COLUMN_2 VARCHAR(50) NOT NULL,
    TEST_COLUMN_3 DATETIME NULL
);
";
        }

        public override string GetSqlForCreateDbObjectWithError(string objectName)
        {
            return $@"
CREATE TABLE {objectName.DoubleQuote()}(
    TEST_COLUMN_1 VARCHAR(50) NOT NULL,
    TEST_COLUMN_2 NONEXISTINGTYPE(50) NOT NULL,
    TEST_COLUMN_3 DATETIME NULL
);
";
        }
        public override string GetSqlForCreateDbObjectWithTokens(string objectName)
        {
            return $@"
CREATE PROCEDURE {$"{objectName}_${{Token1}}_${{Token2}}_${{Token3}}".DoubleQuote()}()
RETURNS VARCHAR
AS
$$
    SELECT '${{Token1}}.${{Token2}}.${{Token3}}' AS ReplacedStatement
$$
";
        }

        public override string GetSqlForCreateBulkTable(string tableName)
        {
            var dbObject = tableName.SplitSchema(_dataService.SchemaName);
            return $@"
CREATE TABLE {dbObject.Item1.DoubleQuote()}.{dbObject.Item2.DoubleQuote()}(
	{"FirstName".DoubleQuote()} VARCHAR(50) NOT NULL,
	{"LastName".DoubleQuote()} VARCHAR(50) NOT NULL,
	{"BirthDate".DoubleQuote()} DATETIME NULL
);
";
        }

        public override string GetSqlForSingleLine(string objectName)
        {
            return $@"
CREATE PROCEDURE {objectName.DoubleQuote()}()
RETURNS INT
AS
$$
    SELECT 1
$$
GO
";
        }

        public override string GetSqlForSingleLineWithoutTerminator(string objectName)
        {
            return $@"
CREATE PROCEDURE {objectName.DoubleQuote()}()
RETURNS INT
AS
$$
    SELECT 1
$$
";
        }

        public override string GetSqlForMultilineWithoutTerminatorInLastLine(string objectName1, string objectName2, string objectName3)
        {
            return $@"
CREATE PROCEDURE {objectName1.DoubleQuote()}()
RETURNS INT
AS
$$
    SELECT 1
$$
GO

CREATE PROCEDURE {objectName2.DoubleQuote()}()
RETURNS INT
AS
$$
    SELECT 1
$$
GO

CREATE PROCEDURE {objectName3.DoubleQuote()}()
RETURNS INT
AS
$$
    SELECT 1
$$
";
        }

        public override string GetSqlForMultilineWithTerminatorInsideStatements(string objectName1, string objectName2, string objectName3)
        {
            return $@"
CREATE PROCEDURE {objectName1.DoubleQuote()}()
RETURNS INT
AS
$$
    --this is a comment with GO as part of the sentence (ALL CAPS)
    SELECT 1
$$
GO

CREATE PROCEDURE {objectName2.DoubleQuote()}()
RETURNS INT
AS
$$
    --this is a comment with go as part of the sentence (small caps)
    SELECT 1
$$
GO

CREATE PROCEDURE {objectName3.DoubleQuote()}()
RETURNS INT
AS
$$
    --this is a comment with Go as part of the sentence (Pascal)
    SELECT 1
$$
";
        }

        public override string GetSqlForMultilineWithError(string objectName1, string objectName2)
        {
            throw new NotSupportedException();
        }

        public override void CreateScriptFile(string sqlFilePath, string sqlStatement)
        {
            using var sw = File.CreateText(sqlFilePath);
            sw.WriteLine(sqlStatement);
        }

        public override string GetSqlForCleanup()
        {
            return $@"
DROP TABLE {"script1".DoubleQuote()};
GO
DROP TABLE {"script2".DoubleQuote()};
GO
DROP TABLE {"script3".DoubleQuote()};
GO
";
        }

        public override void DropDatabase(string connectionString)
        {
            //extract the test database name from connection string
            object databaseName;
            var connectionStringBuilder = new SnowflakeDbConnectionStringBuilder();
            connectionStringBuilder.ConnectionString = connectionString;
            connectionStringBuilder.TryGetValue("db", out databaseName);

            var sqlStatement = $"DROP DATABASE {databaseName.ToString().DoubleQuote()};";

            //prepare a connection to snowflake without any targetdb or schema, we need to remove these keys else it throws an error that db doesn't exists
            var masterConnectionStringBuilder = new SnowflakeDbConnectionStringBuilder();
            masterConnectionStringBuilder.ConnectionString = connectionString;
            masterConnectionStringBuilder.Remove("db");
            masterConnectionStringBuilder.Remove("schema");

            base.ExecuteNonQuery(masterConnectionStringBuilder.ConnectionString, sqlStatement);
        }
        public override string GetSqlForGetBulkTestData(string tableName)
        {
            var dbObject = tableName.SplitSchema(_dataService.SchemaName);
            return $"SELECT * FROM {dbObject.Item1.DoubleQuote()}.{dbObject.Item2.DoubleQuote()};";
        }
    }
}
