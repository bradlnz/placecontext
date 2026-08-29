import csv
import io
import json
import sys
import urllib.parse
import urllib.request

SOURCE = "https://api.open-meteo.com/v1/forecast"


def fetch_json(url):
    request = urllib.request.Request(url, headers={"User-Agent": "PlaceContext-Brisbane-Weather/1.0"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def main():
    payload = json.loads(sys.stdin.read() or "{}")
    latitude = float(payload.get("latitude", -27.4698))
    longitude = float(payload.get("longitude", 153.0251))
    days = max(1, min(int(payload.get("forecast_days", 7)), 16))
    params = {
        "latitude": latitude,
        "longitude": longitude,
        "timezone": "Australia/Brisbane",
        "forecast_days": days,
        "current": "temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,wind_gusts_10m",
        "hourly": "temperature_2m,apparent_temperature,precipitation_probability,precipitation,weather_code,wind_speed_10m,wind_gusts_10m",
        "daily": "weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max,wind_speed_10m_max,wind_gusts_10m_max,sunrise,sunset",
    }
    data = fetch_json(SOURCE + "?" + urllib.parse.urlencode(params))
    daily = data["daily"]
    rows = [
        {key: daily[key][index] for key in daily if key != "time"} | {"date": day}
        for index, day in enumerate(daily["time"])
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
        "daily_forecast": rows,
        "next_24_hours": [
            {key: data["hourly"][key][index] for key in data["hourly"]}
            for index in range(min(24, len(data["hourly"]["time"])))
        ],
        "artifacts": [{"filename": "brisbane-weather-forecast.csv", "content": csv_buffer.getvalue()}],
    }, separators=(",", ":")))


if __name__ == "__main__":
    main()
