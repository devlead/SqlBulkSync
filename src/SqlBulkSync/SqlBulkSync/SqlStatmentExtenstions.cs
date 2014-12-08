// ----------------------------------------------------------------------------------------------
// Copyright (c) WCOM AB.
// ----------------------------------------------------------------------------------------------
// This source code is subject to terms and conditions of the Microsoft Public License. A 
// copy of the license can be found in the LICENSE.md file at the root of this distribution. 
// If you cannot locate the  Microsoft Public License, please send an email to 
// dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
//  by the terms of the Microsoft Public License.
// ----------------------------------------------------------------------------------------------
// You must not remove this notice, or any other, from this software.
// ----------------------------------------------------------------------------------------------
// ReSharper disable ParameterTypeCanBeEnumerable.Global
namespace WCOM.SqlBulkSync
{
    using System;
    using System.Linq;

    public static class SqlStatmentExtenstions
    {
        public static string GetTableVersionStatement(string tableName, Column[] columns)
        {
            return string.Format(
                @"
SELECT  '{0}'                                               AS TableName,
        SYS_CHANGE_VERSION                                  AS CurrentVersion,
        CHANGE_TRACKING_MIN_VALID_VERSION(
            OBJECT_ID('{0}')
        )                                                   AS MinValidVersion,
        GETUTCDATE()                                        AS QueriedDateTime
    FROM  CHANGETABLE(VERSION  {0}, ({1}), ({1})) as t",
                tableName,
                string.Join(
                    ",",
                    columns
                        .Where(column => column.IsPrimary)
                        .Select(column => column.QuoteName)
                    )
                );
        }

        public static string GetDropStatment(this TableSchema tableSchema)
        {
            return string.Format(
                @"DROP TABLE {0}",
                tableSchema.SyncTableName
                );
        }

        public static string GetMergeStatement(this TableSchema tableSchema)
        {
            var identityInsert = tableSchema.Columns.Any(column => column.IsIdentity);
            var statement = string.Format(
                @"{6};
MERGE {0} AS target
USING {1} AS source
ON {2}
WHEN NOT MATCHED BY TARGET
    THEN INSERT (
        {3}
    ) VALUES (
        {4}
    ){8}
WHEN MATCHED
    THEN UPDATE
        SET {5};
SELECT @@ROWCOUNT AS [RowCount];
{7}",
                tableSchema.TableName,
                tableSchema.SyncTableName,
                string.Join(
                    " AND\r\n        ",
                    tableSchema.Columns.Where(column => column.IsPrimary).Select(
                        column => string.Concat(
                            "target.",
                            column.QuoteName,
                            " = source.",
                            column.QuoteName
                            )
                        )
                    ),
                string.Join(
                    ",\r\n        ",
                    tableSchema.Columns.Select(column => column.QuoteName)
                    ),
                string.Join(
                    ",\r\n        ",
                    tableSchema.Columns.Select(column => string.Concat("source.", column.QuoteName))
                    ),
                string.Join(
                    ",\r\n            ",
                    tableSchema.Columns.Where(column => !column.IsPrimary).Select(
                        column => string.Concat(
                            column.QuoteName,
                            " = source.",
                            column.QuoteName
                            )
                        )
                    ),
                (
                    identityInsert
                        ? string.Format(
                            "SET IDENTITY_INSERT {0} ON",
                            tableSchema.TableName
                            )
                        : String.Empty
                    ),
                (
                    identityInsert
                        ? string.Format(
                            "SET IDENTITY_INSERT {0} OFF",
                            tableSchema.TableName
                            )
                        : String.Empty
                    ),
                (
                    (tableSchema.TargetVersion.CurrentVersion <= 1)
                        ? @"
WHEN NOT MATCHED BY SOURCE
    THEN DELETE"
                        : string.Empty
                    )
                );
            return statement;
        }

        public static string GetCreateSyncTableStatement(this TableSchema tableSchema)
        {
            var statement = string.Format(
                @"
CREATE TABLE {0}(
    {1},
 CONSTRAINT [PK_CacheEmpStat] PRIMARY KEY CLUSTERED 
(
    {2}
)
)",
                tableSchema.SyncTableName,
                string.Join(
                    ",\r\n    ",
                    tableSchema.Columns.Select(
                        column => string.Format(
                            "{0} {1} {2}",
                            column.QuoteName,
                            column.Type,
                            column.IsNullable ? "NULL" : "NOT NULL"
                            )
                        )
                    ),
                string.Join(
                    ",\r\n    ",
                    tableSchema.Columns
                        .Where(column => column.IsPrimary)
                        .Select(
                            column => column.QuoteName
                        )
                    )
                );
            return statement;
        }

        public static string GetSourceSelectStatment(this TableSchema tableSchema)
        {
            if (tableSchema.Columns == null || tableSchema.Columns.Length == 0)
                throw new Exception("Columns missing");

            var statement = (tableSchema.TargetVersion.CurrentVersion <= 1)
                ? string.Format(
                    @"SELECT  {0}
    FROM {1} WITH(NOLOCK)",
                    (
                        (tableSchema.Columns == null)
                            ? string.Empty
                            : string.Join(
                                ",\r\n        ",
                                tableSchema.Columns.Select(column => column.QuoteName)
                                )
                        ),
                    tableSchema.TableName
                    )
                : string.Format(
                    @"SELECT  {0}
    FROM CHANGETABLE(CHANGES {1}, {2}) ct
        INNER JOIN {1} t WITH(NOLOCK) ON {3}",
                    (
                        (tableSchema.Columns == null)
                            ? string.Empty
                            : string.Join(
                                ",\r\n        ",
                                tableSchema.Columns.Select(column => string.Concat("t.", column.QuoteName))
                                )
                        ),
                    tableSchema.TableName,
                    tableSchema.TargetVersion.CurrentVersion,
                    string.Join(
                        " AND\r\n        ",
                        tableSchema.Columns.Where(column => column.IsPrimary).Select(
                            column => string.Concat(
                                "t.",
                                column.QuoteName,
                                " = ct.",
                                column.QuoteName
                                )
                            )
                        )
                    );
            return statement;
        }
    }
}