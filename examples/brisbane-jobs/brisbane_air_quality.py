import csv
import io
import json
import sys
import urllib.parse
import urllib.request

SOURCE = "https://air-quality-api.open-meteo.com/v1/air-quality"
FIELDS = "pm10,pm2_5,carbon_monoxide,nitrogen_dioxide,sulphur_dioxide,ozone,aerosol_optical_depth,dust,uv_index,european_aqi"


def main():
    payload = json.loads(sys.stdin.read() or "{}")
    latitude = float(payload.get("latitude", -27.4698))
    longitude = float(payload.get("longitude", 153.0251))
    params = {
        "latitude": latitude,
        "longitude": longitude,
        "timezone": "Australia/Brisbane",
        "forecast_days": 3,
        "current": FIELDS,
        "hourly": FIELDS,
    }
    url = SOURCE + "?" + urllib.parse.urlencode(params)
    request = urllib.request.Request(url, headers={"User-Agent": "PlaceContext-Brisbane-Air-Quality/1.0"})
    with urllib.request.urlopen(request, timeout=30) as response:
        data = json.load(response)
    hourly = data["hourly"]
    rows = [
        {key: hourly[key][index] for key in hourly}
        for index in range(min(48, len(hourly["time"])))
    ]
    csv_buffer = io.StringIO()
    writer = csv.DictWriter(csv_buffer, fieldnames=list(rows[0]))
    writer.writeheader()
    writer.writerows(rows)
    print(json.dumps({
        "location": "Brisbane CBD",
        "coordinates": {"latitude": latitude, "longitude": longitude},
        "timezone": data["timezone"],
        "source": SOURCE,
        "license": "CC BY 4.0",
        "current": data["current"],
        "current_units": data["current_units"],
        "next_48_hours": rows,
        "artifacts": [{"filename": "brisbane-air-quality.csv", "content": csv_buffer.getvalue()}],
    }, separators=(",", ":")))


if __name__ == "__main__":
    main()
