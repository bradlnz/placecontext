namespace PlaceContext.TestSupport;

public static class PostgresProjectAccess
{
    // PostgreSQL resolves filter subqueries even when a service user bypasses project restrictions.
    public const string Schema = """
        CREATE TABLE projects ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL);
        CREATE TABLE users ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "IsDefaultAdmin" boolean NOT NULL DEFAULT false,
            "IsExternalAdmin" boolean NOT NULL DEFAULT false, "Role" text, "AssignedProjectIds" uuid[]);
        """;
}
