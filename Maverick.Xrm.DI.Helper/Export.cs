﻿using Maverick.Xrm.DI.DataObjects;
using Microsoft.Office.Interop.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Maverick.XTB.DI.Helper
{
    public class Export
    {
        #region Private

        private static List<string> ColumnHeaders = new List<string> { "\"Entity Schema Name\"", "\"Dependent Component\"", "\"Dependent Component Type\"", "\"DependentDescription\"" };

        private static void FormatAsTable(Range sourceRange, string tableName, string tableStyleName)
        {
            sourceRange.Worksheet.ListObjects.Add(XlListObjectSourceType.xlSrcRange,
                    sourceRange, Type.Missing, XlYesNoGuess.xlYes, Type.Missing).Name =
                tableName;
            sourceRange.Select();
            sourceRange.Worksheet.ListObjects[tableName].TableStyle = tableStyleName;
        }

        #endregion

        public static void ExportAsCsv(List<DependencyReport> rows, string fileName)
        {
            var writer = new StreamWriter(fileName);

            var header = string.Join(",", ColumnHeaders);
            writer.WriteLine(header);

            foreach (var row in rows.Where(r => r.SkipAdding == false))
            {
                writer.WriteLine($"\"{row.EntitySchemaName}\",\"{row.DependentComponentName}\",\"{row.DependentComponentType}\",\"{row.DependentDescription}\"");
            }

            writer.Close();
        }

        public static void ExportAsExcel(List<DependencyReport> rows, string fileName)
        {
            var excel = new Microsoft.Office.Interop.Excel.Application();
            var workbook = excel.Workbooks.Add();
            Worksheet worksheet = workbook.Sheets.Add();
            worksheet.Name = "Dependencies";

            worksheet.Cells[1, 1].Value2 = ColumnHeaders[0].Replace("\"","");
            worksheet.Cells[1, 2].Value2 = ColumnHeaders[1].Replace("\"", "");
            worksheet.Cells[1, 3].Value2 = ColumnHeaders[2].Replace("\"", "");
            worksheet.Cells[1, 4].Value2 = ColumnHeaders[3].Replace("\"", "");

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];

                worksheet.Cells[index + 2, "A"].Value2 = row.EntitySchemaName;
                worksheet.Cells[index + 2, "B"].Value2 = row.DependentComponentName;
                worksheet.Cells[index + 2, "C"].Value2 = row.DependentComponentType;
                worksheet.Cells[index + 2, "D"].Value2 = row.DependentDescription;
            }

            var range = worksheet.Range["A1", $"D{rows.Count + 1}"];
            FormatAsTable(range, "TableDependency", "TableStyleMedium9");

            range.Columns.AutoFit();

            excel.DisplayAlerts = false;
            workbook.SaveAs(fileName, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, XlSaveAsAccessMode.xlNoChange, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
            workbook.Close(true);
            excel.Quit();
        }
    }
}
