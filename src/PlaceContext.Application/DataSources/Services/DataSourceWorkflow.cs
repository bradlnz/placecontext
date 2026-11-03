namespace PlaceContext.Application.DataSources;

public static class DataSourceWorkflow
{
    public const string Script = """
        import json
        import os
        import sys
        import urllib.request

        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, request, fp, code, msg, headers, newurl):
                return None

        url = os.environ["PC_HOST"].rstrip("/") + "/api/v1/job-data/sync-source"
        request = urllib.request.Request(url, data=b"{}", method="POST", headers={
            "Authorization": "Bearer " + os.environ["PC_DATA_TOKEN"],
            "Content-Type": "application/json",
        })
        try:
            with urllib.request.build_opener(NoRedirect).open(request, timeout=900) as response:
                result = json.load(response)
            print(json.dumps(result))
        except Exception:
            print("Source sync failed. Check source configuration and portal connectivity.", file=sys.stderr)
            sys.exit(1)
        """;
}
