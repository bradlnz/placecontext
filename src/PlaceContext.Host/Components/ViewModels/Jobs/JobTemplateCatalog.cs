using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public static class JobTemplateCatalog
{
    public static IReadOnlyList<JobTemplate> All { get; } = new List<JobTemplate>
    {
        HubSpotContacts(),
        XeroInvoices(),
        ShopifyOrders(),
        PostgresQuery(),
        MySqlQuery(),
        SqlServerQuery(),
        MongoDbExport(),
        SnowflakeQuery(),
        BigQueryQuery(),
        RestApiPoller(),
        WebhookReceiver(),
        CsvToDatabase(),
        EmailReport(),
    };

    public static JobTemplate? GetById(string id) =>
        All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> Categories =>
        All.Select(t => t.Category).Distinct().ToList();

    public static IReadOnlyList<JobTemplate> ByCategory(string category) =>
        All.Where(t => t.Category == category).ToList();

    // ── SaaS integrations ───────────────────────────────────────────────────────────────────────

    private static JobTemplate HubSpotContacts() => new(
        Id: "hubspot-contacts",
        Name: "HubSpot contact sync",
        Category: "Integrations",
        Description: "Pull recently updated contacts from HubSpot and return them as JSON. Add your Private App access token to the vault.",
        Icon: "◎",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: HubSpotSource,
        MapEnvRaw: "HUBSPOT_ACCESS_TOKEN=\nHUBSPOT_LIMIT=100",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("HubSpot Private App token", "A HubSpot Private App access token with crm.objects.contacts.read scope.", "HUBSPOT_ACCESS_TOKEN")
        });

    private static JobTemplate XeroInvoices() => new(
        Id: "xero-invoices",
        Name: "Xero invoice export",
        Category: "Integrations",
        Description: "Fetch a page of invoices from Xero for the configured tenant. Uses OAuth 2 client credentials flow.",
        Icon: "✕",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: XeroSource,
        MapEnvRaw: "XERO_CLIENT_ID=\nXERO_CLIENT_SECRET=\nXERO_TENANT_ID=",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("Xero client ID", "The client ID from your Xero OAuth 2 app.", "XERO_CLIENT_ID"),
            new JobCredentialRequirement("Xero client secret", "The client secret from your Xero OAuth 2 app.", "XERO_CLIENT_SECRET"),
            new JobCredentialRequirement("Xero tenant ID", "The Xero organisation tenant ID to query.", "XERO_TENANT_ID")
        });

    private static JobTemplate ShopifyOrders() => new(
        Id: "shopify-orders",
        Name: "Shopify order export",
        Category: "Integrations",
        Description: "Pull the latest orders from a Shopify store using the Admin API.",
        Icon: "S",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: ShopifySource,
        MapEnvRaw: "SHOPIFY_SHOP_DOMAIN=my-store.myshopify.com\nSHOPIFY_ACCESS_TOKEN=\nSHOPIFY_LIMIT=50",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("Shopify Admin API access token", "A Shopify Admin API access token with read_orders scope.", "SHOPIFY_ACCESS_TOKEN"),
            new JobCredentialRequirement("Shopify shop domain", "The store's .myshopify.com domain.", "SHOPIFY_SHOP_DOMAIN")
        });

    // ── Databases ───────────────────────────────────────────────────────────────────────────────

    private static JobTemplate PostgresQuery() => new(
        Id: "postgres-query",
        Name: "PostgreSQL query",
        Category: "Databases",
        Description: "Run a SQL query against PostgreSQL and return the rows as JSON. Uses the DATABASE_URL environment variable.",
        Icon: "🐘",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: PostgresSource,
        MapEnvRaw: "DATABASE_URL=postgresql://user:pass@host:5432/db\nSQL_QUERY=SELECT * FROM table LIMIT 100",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("PostgreSQL connection string", "A full DATABASE_URL including credentials for the target database.", "DATABASE_URL")
        });

    private static JobTemplate MySqlQuery() => new(
        Id: "mysql-query",
        Name: "MySQL query",
        Category: "Databases",
        Description: "Run a SQL query against MySQL and return the rows as JSON. Uses the DATABASE_URL environment variable.",
        Icon: "🐬",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: MySqlSource,
        MapEnvRaw: "DATABASE_URL=mysql://user:pass@host:3306/db\nSQL_QUERY=SELECT * FROM table LIMIT 100",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("MySQL connection string", "A full DATABASE_URL including credentials for the target database.", "DATABASE_URL")
        });

    private static JobTemplate SqlServerQuery() => new(
        Id: "sqlserver-query",
        Name: "SQL Server query",
        Category: "Databases",
        Description: "Run a SQL query against SQL Server and return the rows as JSON. Uses the DATABASE_URL environment variable.",
        Icon: "🗄",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: SqlServerSource,
        MapEnvRaw: "DATABASE_URL=sqlserver://user:pass@host:1433/db\nSQL_QUERY=SELECT TOP 100 * FROM table",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("SQL Server connection string", "A full DATABASE_URL including credentials for the target database.", "DATABASE_URL")
        });

    private static JobTemplate MongoDbExport() => new(
        Id: "mongodb-export",
        Name: "MongoDB collection export",
        Category: "Databases",
        Description: "Query a MongoDB collection and return matching documents as JSON.",
        Icon: "🍃",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: MongoDbSource,
        MapEnvRaw: "MONGODB_URI=mongodb://user:pass@host:27017/db\nMONGODB_COLLECTION=items\nMONGODB_LIMIT=100",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("MongoDB URI", "A MongoDB connection URI with embedded credentials.", "MONGODB_URI")
        });

    private static JobTemplate SnowflakeQuery() => new(
        Id: "snowflake-query",
        Name: "Snowflake query",
        Category: "Databases",
        Description: "Run a query against Snowflake and return the result set as JSON.",
        Icon: "❄",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: SnowflakeSource,
        MapEnvRaw: "SNOWFLAKE_ACCOUNT=xy12345\nSNOWFLAKE_USER=\nSNOWFLAKE_PASSWORD=\nSNOWFLAKE_DATABASE=\nSNOWFLAKE_WAREHOUSE=\nSQL_QUERY=SELECT * FROM table LIMIT 100",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("Snowflake account", "Your Snowflake account identifier (e.g. xy12345.region).", "SNOWFLAKE_ACCOUNT"),
            new JobCredentialRequirement("Snowflake username", "The username to authenticate with.", "SNOWFLAKE_USER"),
            new JobCredentialRequirement("Snowflake password", "The password for the Snowflake user.", "SNOWFLAKE_PASSWORD")
        });

    private static JobTemplate BigQueryQuery() => new(
        Id: "bigquery-query",
        Name: "BigQuery query",
        Category: "Databases",
        Description: "Run a query against Google BigQuery using a service account. Store the service account JSON in the vault.",
        Icon: "B",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: BigQuerySource,
        MapEnvRaw: "GOOGLE_APPLICATION_CREDENTIALS_JSON=\nGOOGLE_PROJECT_ID=\nSQL_QUERY=SELECT * FROM dataset.table LIMIT 100",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("Google service account JSON", "The JSON key of a service account with BigQuery Data Viewer / Job User roles.", "GOOGLE_APPLICATION_CREDENTIALS_JSON"),
            new JobCredentialRequirement("Google Cloud project ID", "The project ID that owns the BigQuery dataset.", "GOOGLE_PROJECT_ID")
        });

    // ── Common patterns ─────────────────────────────────────────────────────────────────────────

    private static JobTemplate RestApiPoller() => new(
        Id: "rest-api-poller",
        Name: "REST API poller",
        Category: "Common patterns",
        Description: "Poll any REST endpoint with an API key and return the JSON response. Add credentials and customise the URL.",
        Icon: "↻",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: RestApiSource,
        MapEnvRaw: "API_BASE_URL=https://api.example.com\nAPI_KEY=",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("API key / bearer token", "The API key or bearer token used to authenticate requests.", "API_KEY")
        });

    private static JobTemplate WebhookReceiver() => new(
        Id: "webhook-receiver",
        Name: "Webhook receiver",
        Category: "Common patterns",
        Description: "A starter job that echoes and validates a webhook payload. Pair with an Event trigger to run automatically.",
        Icon: "⇄",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: WebhookSource,
        MapEnvRaw: "WEBHOOK_SECRET=",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: false,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("Webhook secret (optional)", "A shared secret used to validate webhook signatures. Optional but recommended.", "WEBHOOK_SECRET")
        });

    private static JobTemplate CsvToDatabase() => new(
        Id: "csv-to-database",
        Name: "CSV import to database",
        Category: "Common patterns",
        Description: "Download a CSV file and insert its rows into a database table. Configure the URL, table and connection string.",
        Icon: "CSV",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: CsvToDbSource,
        MapEnvRaw: "CSV_URL=https://example.com/data.csv\nDATABASE_URL=\nTARGET_TABLE=data_import",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("Database connection string", "A connection string for the target database.", "DATABASE_URL")
        });

    private static JobTemplate EmailReport() => new(
        Id: "email-report",
        Name: "Scheduled email report",
        Category: "Common patterns",
        Description: "Generate a simple HTML report and email it via SMTP. Wire this to a schedule trigger for daily/weekly reports.",
        Icon: "✉",
        MapSourceKind: "code",
        MapRuntimeId: "node",
        MapEntrypoint: "index.js",
        MapSource: EmailReportSource,
        MapEnvRaw: "SMTP_HOST=\nSMTP_PORT=587\nSMTP_USER=\nSMTP_PASS=\nSMTP_FROM=\nSMTP_TO=",
        InputPayloadsRaw: "{}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Html,
        AllowNetworkEgress: true,
        RequiredCredentials: new[]
        {
            new JobCredentialRequirement("SMTP host", "The SMTP server hostname.", "SMTP_HOST"),
            new JobCredentialRequirement("SMTP username", "The username for SMTP authentication.", "SMTP_USER"),
            new JobCredentialRequirement("SMTP password", "The password for SMTP authentication.", "SMTP_PASS")
        });

    // ── Starter source code ─────────────────────────────────────────────────────────────────────

    private const string HubSpotSource = @"const fs = require('fs');
const token = process.env.HUBSPOT_ACCESS_TOKEN;
const limit = parseInt(process.env.HUBSPOT_LIMIT || '100', 10);

async function main() {
  if (!token) throw new Error('Missing HUBSPOT_ACCESS_TOKEN');
  const res = await fetch(`https://api.hubapi.com/crm/v3/objects/contacts?limit=${limit}&properties=email,firstname,lastname,phone`, {
    headers: { Authorization: `Bearer ${token}` }
  });
  if (!res.ok) throw new Error(`HubSpot ${res.status}: ${await res.text()}`);
  const data = await res.json();
  process.stdout.write(JSON.stringify(data));
}

main().catch(e => { console.error(e); process.exit(1); });";

    private const string XeroSource = @"const fs = require('fs');
const clientId = process.env.XERO_CLIENT_ID;
const clientSecret = process.env.XERO_CLIENT_SECRET;
const tenantId = process.env.XERO_TENANT_ID;

async function getToken() {
  const res = await fetch('https://identity.xero.com/connect/token', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      Authorization: 'Basic ' + Buffer.from(`${clientId}:${clientSecret}`).toString('base64')
    },
    body: 'grant_type=client_credentials'
  });
  if (!res.ok) throw new Error(`Xero token ${res.status}: ${await res.text()}`);
  return (await res.json()).access_token;
}

async function main() {
  if (!clientId || !clientSecret || !tenantId) throw new Error('Missing Xero credentials');
  const token = await getToken();
  const res = await fetch('https://api.xero.com/api.xro/2.0/Invoices', {
    headers: {
      Authorization: `Bearer ${token}`,
      'Xero-tenant-id': tenantId
    }
  });
  if (!res.ok) throw new Error(`Xero ${res.status}: ${await res.text()}`);
  const data = await res.json();
  process.stdout.write(JSON.stringify(data));
}

main().catch(e => { console.error(e); process.exit(1); });";

    private const string ShopifySource = @"const token = process.env.SHOPIFY_ACCESS_TOKEN;
const shop = process.env.SHOPIFY_SHOP_DOMAIN;
const limit = parseInt(process.env.SHOPIFY_LIMIT || '50', 10);

async function main() {
  if (!token || !shop) throw new Error('Missing SHOPIFY_ACCESS_TOKEN or SHOPIFY_SHOP_DOMAIN');
  const res = await fetch(`https://${shop}/admin/api/2024-04/orders.json?limit=${limit}&status=any`, {
    headers: { 'X-Shopify-Access-Token': token }
  });
  if (!res.ok) throw new Error(`Shopify ${res.status}: ${await res.text()}`);
  const data = await res.json();
  process.stdout.write(JSON.stringify(data));
}

main().catch(e => { console.error(e); process.exit(1); });";

    private const string PostgresSource = @"// Install pg via requirements.txt or package.json: npm install pg
const { Client } = require('pg');

async function main() {
  if (!process.env.DATABASE_URL) throw new Error('Missing DATABASE_URL');
  const client = new Client({ connectionString: process.env.DATABASE_URL });
  await client.connect();
  const res = await client.query(process.env.SQL_QUERY || 'SELECT now()');
  process.stdout.write(JSON.stringify(res.rows));
  await client.end();
}

main().catch(e => { console.error(e); process.exit(1); });";

    private const string MySqlSource = @"// Install mysql2 via requirements.txt or package.json: npm install mysql2
const mysql = require('mysql2/promise');

async function main() {
  if (!process.env.DATABASE_URL) throw new Error('Missing DATABASE_URL');
  const conn = await mysql.createConnection(process.env.DATABASE_URL);
  const [rows] = await conn.execute(process.env.SQL_QUERY || 'SELECT now()');
  process.stdout.write(JSON.stringify(rows));
  await conn.end();
}

main().catch(e => { console.error(e); process.exit(1); });";

    private const string SqlServerSource = @"// Install mssql via requirements.txt or package.json: npm install mssql
const sql = require('mssql');

async function main() {
  if (!process.env.DATABASE_URL) throw new Error('Missing DATABASE_URL');
  const pool = await sql.connect(process.env.DATABASE_URL);
  const res = await pool.request().query(process.env.SQL_QUERY || 'SELECT GETDATE()');
  process.stdout.write(JSON.stringify(res.recordset));
  await pool.close();
}

main().catch(e => { console.error(e); process.exit(1); });";

    private const string MongoDbSource = @"// Install mongodb via requirements.txt or package.json: npm install mongodb
const { MongoClient } = require('mongodb');

async function main() {
  if (!process.env.MONGODB_URI) throw new Error('Missing MONGODB_URI');
  const client = new MongoClient(process.env.MONGODB_URI);
  await client.connect();
  const db = client.db();
  const coll = db.collection(process.env.MONGODB_COLLECTION || 'items');
  const docs = await coll.find({}).limit(parseInt(process.env.MONGODB_LIMIT || '100', 10)).toArray();
  process.stdout.write(JSON.stringify(docs));
  await client.close();
}

main().catch(e => { console.error(e); process.exit(1); });";

    private const string SnowflakeSource = @"// Install snowflake-sdk via package.json: npm install snowflake-sdk
const snowflake = require('snowflake-sdk');

function executeSql(conn, sqlText) {
  return new Promise((resolve, reject) => {
    conn.execute({ sqlText, complete: (err, stmt, rows) => err ? reject(err) : resolve(rows) });
  });
}

async function main() {
  const required = ['SNOWFLAKE_ACCOUNT','SNOWFLAKE_USER','SNOWFLAKE_PASSWORD','SNOWFLAKE_DATABASE','SNOWFLAKE_WAREHOUSE'];
  const missing = required.filter(k => !process.env[k]);
  if (missing.length) throw new Error('Missing: ' + missing.join(', '));
  const conn = snowflake.createConnection({
    account: process.env.SNOWFLAKE_ACCOUNT,
    username: process.env.SNOWFLAKE_USER,
    password: process.env.SNOWFLAKE_PASSWORD,
    database: process.env.SNOWFLAKE_DATABASE,
    warehouse: process.env.SNOWFLAKE_WAREHOUSE
  });
  await new Promise((resolve, reject) => conn.connect((err) => err ? reject(err) : resolve()));
  const rows = await executeSql(conn, process.env.SQL_QUERY || 'SELECT CURRENT_TIMESTAMP()');
  process.stdout.write(JSON.stringify(rows));
  await new Promise((resolve, reject) => conn.destroy((err) => err ? reject(err) : resolve()));
}

main().catch(e => { console.error(e); process.exit(1); });";

    private const string BigQuerySource = @"// Install @google-cloud/bigquery via package.json: npm install @google-cloud/bigquery
const { BigQuery } = require('@google-cloud/bigquery');
const fs = require('fs');

async function main() {
  if (!process.env.GOOGLE_APPLICATION_CREDENTIALS_JSON) throw new Error('Missing GOOGLE_APPLICATION_CREDENTIALS_JSON');
  if (!process.env.GOOGLE_PROJECT_ID) throw new Error('Missing GOOGLE_PROJECT_ID');
  const keyFile = '/tmp/gcp-key.json';
  fs.writeFileSync(keyFile, process.env.GOOGLE_APPLICATION_CREDENTIALS_JSON);
  const bq = new BigQuery({ projectId: process.env.GOOGLE_PROJECT_ID, keyFilename: keyFile });
  const [job] = await bq.createQueryJob({ query: process.env.SQL_QUERY || 'SELECT 1' });
  const [rows] = await job.getQueryResults();
  process.stdout.write(JSON.stringify(rows));
}

main().catch(e => { console.error(e); process.exit(1); });";

    private const string RestApiSource = @"const baseUrl = process.env.API_BASE_URL;
const apiKey = process.env.API_KEY;

async function main() {
  if (!baseUrl) throw new Error('Missing API_BASE_URL');
  const res = await fetch(`${baseUrl.replace(/\/$/, '')}/resource`, {
    headers: apiKey ? { Authorization: `Bearer ${apiKey}` } : {}
  });
  if (!res.ok) throw new Error(`API ${res.status}: ${await res.text()}`);
  const data = await res.json();
  process.stdout.write(JSON.stringify(data));
}

main().catch(e => { console.error(e); process.exit(1); });";

    private const string WebhookSource = @"const crypto = require('crypto');

function main() {
  const data = require('fs').readFileSync('/dev/stdin', 'utf8');
  const payload = JSON.parse(data || '{}');
  const secret = process.env.WEBHOOK_SECRET;
  const signature = process.env.WEBHOOK_SIGNATURE || '';

  if (secret && signature) {
    const expected = crypto.createHmac('sha256', secret).update(data).digest('hex');
    if (!crypto.timingSafeEqual(Buffer.from(signature), Buffer.from(expected))) {
      throw new Error('Invalid webhook signature');
    }
  }

  process.stdout.write(JSON.stringify({ received: true, payload }));
}

main();";

    private const string CsvToDbSource = @"// Install pg and csv-parse via package.json: npm install pg csv-parse
const fs = require('fs');
const { parse } = require('csv-parse/sync');
const { Client } = require('pg');

async function main() {
  if (!process.env.CSV_URL || !process.env.DATABASE_URL || !process.env.TARGET_TABLE) {
    throw new Error('Missing CSV_URL, DATABASE_URL or TARGET_TABLE');
  }
  const res = await fetch(process.env.CSV_URL);
  if (!res.ok) throw new Error(`CSV ${res.status}`);
  const records = parse(await res.text(), { columns: true, skip_empty_lines: true });
  const client = new Client({ connectionString: process.env.DATABASE_URL });
  await client.connect();
  // Example: insert records. Adjust columns to match your table.
  for (const r of records.slice(0, 100)) {
    await client.query(`INSERT INTO ${process.env.TARGET_TABLE} (data) VALUES ($1)`, [JSON.stringify(r)]);
  }
  await client.end();
  process.stdout.write(JSON.stringify({ imported: records.length }));
}

main().catch(e => { console.error(e); process.exit(1); });";

    private const string EmailReportSource = @"// Install nodemailer via package.json: npm install nodemailer
const nodemailer = require('nodemailer');

async function main() {
  const required = ['SMTP_HOST','SMTP_PORT','SMTP_USER','SMTP_PASS','SMTP_FROM','SMTP_TO'];
  const missing = required.filter(k => !process.env[k]);
  if (missing.length) throw new Error('Missing: ' + missing.join(', '));
  const transporter = nodemailer.createTransport({
    host: process.env.SMTP_HOST,
    port: parseInt(process.env.SMTP_PORT, 10),
    auth: { user: process.env.SMTP_USER, pass: process.env.SMTP_PASS }
  });
  const html = `<h1>Report</h1><p>Generated at ${new Date().toISOString()}</p>`;
  const info = await transporter.sendMail({
    from: process.env.SMTP_FROM,
    to: process.env.SMTP_TO,
    subject: 'Scheduled report',
    html
  });
  process.stdout.write(html);
}

main().catch(e => { console.error(e); process.exit(1); });";
}
