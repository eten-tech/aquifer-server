using Aquifer.API.Common;
using Aquifer.API.Services;
using Aquifer.Data;
using FastEndpoints;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.API.Endpoints.Reports.Dynamic.Get;

public class Endpoint(AquiferDbContext dbContext, IUserService userService) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/reports/dynamic/{Slug}");
        Permissions(PermissionName.ReadReports, PermissionName.ReadReportsInCompany);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var report = await dbContext.Reports.SingleOrDefaultAsync(r => r.Slug == request.Slug, ct);

        if (report == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var user = await userService.GetUserFromJwtAsync(ct);
        var userPermissions = userService.GetAllJwtPermissions();
        
        var hasGlobalReportAccess = userPermissions.Contains(PermissionName.ReadReports);
        
        // If only has company-scoped permission, restrict to own company
        if (!hasGlobalReportAccess &&
            user.CompanyId != request.CompanyId &&
            request.CompanyId != 0)
        {
            await SendForbiddenAsync(ct);
            return;
        }

        await using var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = report.SqlStatement;

        DateOnly? startDate = null;
        DateOnly? endDate = null;

        if (report.AcceptsDateRange)
        {
            startDate = request.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-(report.DefaultDateRangeMonths ?? 3)));
            endDate = request.EndDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            command.Parameters.Add(new SqlParameter("@StartDate", startDate.Value));
            command.Parameters.Add(new SqlParameter("@EndDate", endDate.Value));
        }

        if (report.AcceptsLanguage)
        {
            command.Parameters.Add(new SqlParameter("@LanguageId", request.LanguageId));
        }

        if (report.AcceptsParentResource)
        {
            command.Parameters.Add(new SqlParameter("@ParentResourceId", request.ParentResourceId));
        }

        if (report.AcceptsCompany)
        {
            command.Parameters.Add(new SqlParameter("@CompanyId", request.CompanyId));
        }

        var items = new List<List<object?>>();
        var columnNames = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(ct);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            columnNames.Add(reader.GetName(i));
        }

        while (await reader.ReadAsync(ct))
        {
            var item = new List<object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                item.Add(await reader.IsDBNullAsync(i, ct) ? null : reader[i]);
            }

            items.Add(item);
        }

        var response = new Response
        {
            Name = report.Name,
            Description = report.Description,
            Type = report.Type,
            AcceptsDateRange = report.AcceptsDateRange,
            AcceptsLanguage = report.AcceptsLanguage,
            AcceptsParentResource = report.AcceptsParentResource,
            AcceptsCompany = report.AcceptsCompany,
            StartDate = startDate,
            EndDate = endDate,
            Columns = columnNames,
            Results = items,
        };

        await SendOkAsync(response, ct);
    }
}