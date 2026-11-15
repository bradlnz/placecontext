using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlaceContext.Infrastructure.Persistence;
namespace PlaceContext.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261115090000_AddWorkflowSchedules")]
public sealed class AddWorkflowSchedules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("WorkflowId", "job_triggers", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>("ScheduledBy", "job_triggers", type: "uuid", nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("WorkflowId", "job_triggers");
        migrationBuilder.DropColumn("ScheduledBy", "job_triggers");
    }
}
