package main

import (
	"strings"
	"testing"
)

func TestChartFromNumberArray(t *testing.T) {
	out := chartFromJSON(`[3, 1, 4]`)
	if out == "" || !strings.Contains(out, "█") {
		t.Fatalf("expected bars for a number array, got %q", out)
	}
	if !strings.Contains(out, "4") {
		t.Fatalf("expected the max value shown, got %q", out)
	}
}

func TestChartFromObjectArrayPrefersKnownFields(t *testing.T) {
	out := chartFromJSON(`[{"day":"mon","total":12},{"day":"tue","total":31},{"day":"wed","total":7}]`)
	for _, want := range []string{"mon", "tue", "wed", "31"} {
		if !strings.Contains(out, want) {
			t.Fatalf("chart missing %q:\n%s", want, out)
		}
	}
	// tue (31) must have the longest bar
	lines := strings.Split(strings.TrimSpace(out), "\n")
	longest := ""
	for _, l := range lines {
		if strings.Count(l, "█") > strings.Count(longest, "█") {
			longest = l
		}
	}
	if !strings.HasPrefix(longest, "tue") {
		t.Fatalf("expected tue to have the longest bar:\n%s", out)
	}
}

func TestChartFromNumericMapAndWrapper(t *testing.T) {
	if out := chartFromJSON(`{"mon":12,"tue":31}`); !strings.Contains(out, "mon") {
		t.Fatalf("numeric map should chart, got %q", out)
	}
	if out := chartFromJSON(`{"series":[{"name":"a","value":1},{"name":"b","value":2}]}`); !strings.Contains(out, "b") {
		t.Fatalf("wrapped series should chart, got %q", out)
	}
}

func TestChartRejectsNonSeries(t *testing.T) {
	for _, in := range []string{
		"not json at all",
		`"just a string"`,
		`{"message":"hello","ok":true}`,
		`[42]`,                  // one number isn't a chart
		`[{"a":"x"},{"a":"y"}]`, // no numeric field
		``,
	} {
		if out := chartFromJSON(in); out != "" {
			t.Fatalf("expected no chart for %q, got %q", in, out)
		}
	}
}

func TestChartCapsRows(t *testing.T) {
	var sb strings.Builder
	sb.WriteString("[")
	for i := 0; i < 40; i++ {
		if i > 0 {
			sb.WriteString(",")
		}
		sb.WriteString("1")
	}
	sb.WriteString("]")
	out := chartFromJSON(sb.String())
	if !strings.Contains(out, "… 24 more") {
		t.Fatalf("expected overflow note, got:\n%s", out)
	}
}

func TestRunDetailIncludesChartForJsonArtifact(t *testing.T) {
	shards := `[{"index":0,"exitCode":0,"outcome":"Succeeded",` +
		`"artifact":"[{\"day\":\"mon\",\"total\":12},{\"day\":\"tue\",\"total\":31}]","log":"done"}]`
	out := renderRunDetail(runRow{id: "12345678-aaaa-bbbb-cccc-000000000000", status: "Succeeded"}, shards, "")
	if !strings.Contains(out, "**chart**") || !strings.Contains(out, "█") {
		t.Fatalf("run detail should chart a JSON artifact:\n%s", out)
	}
	if !strings.Contains(out, "tue") {
		t.Fatalf("chart labels missing:\n%s", out)
	}
}
