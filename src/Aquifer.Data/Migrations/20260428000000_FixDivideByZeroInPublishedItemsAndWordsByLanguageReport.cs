using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aquifer.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixDivideByZeroInPublishedItemsAndWordsByLanguageReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE Reports
                SET SqlStatement = ';WITH EnglishCounts AS (SELECT PR.Id,COUNT(DISTINCT RC.Id) AS ItemCount,ISNULL(SUM(RCV.WordCount),0) AS WordCount FROM ResourceContentVersions RCV INNER JOIN ResourceContents RC ON RC.Id=RCV.ResourceContentId INNER JOIN Resources R ON R.Id=RC.ResourceId INNER JOIN ParentResources PR ON PR.Id=R.ParentResourceId INNER JOIN Languages LAN ON LAN.Id=RC.LanguageId WHERE RCV.IsPublished=1 AND LAN.Id=1 AND(@ParentResourceId=0 OR @ParentResourceId=PR.Id) GROUP BY PR.Id)
SELECT PR.DisplayName AS Resource,LAN.EnglishDisplay AS Language,COUNT(DISTINCT RC.Id) AS ItemCount,SUM(RCV.WordCount) AS WordCount,FORMAT(SUM(RCV.SourceWordCount)/CAST(NULLIF(EC.WordCount, 0) AS decimal),''P'') AS [% Complete]
FROM ResourceContentVersions RCV INNER JOIN ResourceContents RC ON RC.Id=RCV.ResourceContentId INNER JOIN Resources R ON R.Id=RC.ResourceId INNER JOIN ParentResources PR ON PR.Id=R.ParentResourceId INNER JOIN Languages LAN ON LAN.Id=RC.LanguageId INNER JOIN EnglishCounts EC ON EC.Id=PR.Id
WHERE RCV.IsPublished=1 AND(@LanguageId=0 OR @LanguageId=LAN.Id) AND(@ParentResourceId=0 OR @ParentResourceId=PR.Id)
GROUP BY PR.DisplayName,LAN.EnglishDisplay,EC.WordCount
ORDER BY PR.DisplayName,LAN.EnglishDisplay'
                WHERE Slug = 'published-items-and-words-by-language'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE Reports
                SET SqlStatement = ';WITH EnglishCounts AS (SELECT PR.Id,COUNT(DISTINCT RC.Id) AS ItemCount,ISNULL(SUM(RCV.WordCount),0) AS WordCount FROM ResourceContentVersions RCV INNER JOIN ResourceContents RC ON RC.Id=RCV.ResourceContentId INNER JOIN Resources R ON R.Id=RC.ResourceId INNER JOIN ParentResources PR ON PR.Id=R.ParentResourceId INNER JOIN Languages LAN ON LAN.Id=RC.LanguageId WHERE RCV.IsPublished=1 AND LAN.Id=1 AND(@ParentResourceId=0 OR @ParentResourceId=PR.Id) GROUP BY PR.Id)
SELECT PR.DisplayName AS Resource,LAN.EnglishDisplay AS Language,COUNT(DISTINCT RC.Id) AS ItemCount,SUM(RCV.WordCount) AS WordCount,FORMAT(SUM(RCV.SourceWordCount)/CAST(EC.WordCount AS decimal),''P'') AS [% Complete]
FROM ResourceContentVersions RCV INNER JOIN ResourceContents RC ON RC.Id=RCV.ResourceContentId INNER JOIN Resources R ON R.Id=RC.ResourceId INNER JOIN ParentResources PR ON PR.Id=R.ParentResourceId INNER JOIN Languages LAN ON LAN.Id=RC.LanguageId INNER JOIN EnglishCounts EC ON EC.Id=PR.Id
WHERE RCV.IsPublished=1 AND(@LanguageId=0 OR @LanguageId=LAN.Id) AND(@ParentResourceId=0 OR @ParentResourceId=PR.Id)
GROUP BY PR.DisplayName,LAN.EnglishDisplay,EC.WordCount
ORDER BY PR.DisplayName,LAN.EnglishDisplay'
                WHERE Slug = 'published-items-and-words-by-language'
                """);
        }
    }
}
