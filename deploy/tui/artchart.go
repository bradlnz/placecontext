package main

// Jobs exist to generate artifacts — and when an artifact is JSON carrying a numeric series,
// the run detail renders it as a chart right in the terminal, so the data is seen, not just
// stored. Shapes understood:
//
//	[3, 1, 4]                                   → indexed bars
//	[{"day":"mon","total":12}, …]               → label field + value field per object
//	{"mon":12, "tue":31}                        → key/value bars (sorted by key)
//	{"data":[…]} / {"series":[…]} / {"rows":[…]} → unwrapped one level, then as above
//
// Anything else (non-JSON, strings, deeply nested) renders nothing — the raw artifact is
// already shown above the chart.

import (
	"encoding/json"
	"fmt"
	"math"
	"sort"
	"strings"
)

const (
	chartMaxRows  = 16
	chartBarWidth = 30
	chartLabelMax = 16
)

// chartFromJSON returns an ASCII bar chart for a JSON numeric series, or "" if the content
// doesn't hold one. Safe on arbitrary input.
func chartFromJSON(content string) string {
	content = strings.TrimSpace(content)
	if content == "" || (content[0] != '{' && content[0] != '[') {
		return "" // fast path: not JSON-shaped
	}
	var v any
	if err := json.Unmarshal([]byte(content), &v); err != nil {
		return ""
	}
	labels, values := extractSeries(v, 0)
	if len(values) < 2 || len(labels) != len(values) {
		return "" // one number isn't a chart
	}
	return renderBarChart(labels, values)
}

// preferred field names, in order, when picking label/value fields from object rows.
var labelKeys = []string{"label", "name", "key", "date", "day", "month", "hour", "id", "title"}
var valueKeys = []string{"value", "count", "total", "amount", "sum", "avg", "n", "qty"}

// extractSeries pulls (labels, values) out of decoded JSON. depth guards the one-level unwrap.
func extractSeries(v any, depth int) ([]string, []float64) {
	switch t := v.(type) {
	case []any:
		if len(t) == 0 {
			return nil, nil
		}
		// array of numbers → indexed series
		if _, ok := t[0].(float64); ok {
			var labels []string
			var vals []float64
			for i, e := range t {
				f, ok := e.(float64)
				if !ok {
					return nil, nil
				}
				labels = append(labels, fmt.Sprintf("%d", i))
				vals = append(vals, f)
			}
			return labels, vals
		}
		// array of objects → find a label field and a value field
		if _, ok := t[0].(map[string]any); ok {
			return seriesFromObjects(t)
		}
		return nil, nil

	case map[string]any:
		// all-numeric map → key/value bars, keys sorted for a stable chart
		if labels, vals, ok := numericMap(t); ok {
			return labels, vals
		}
		// container object → unwrap the first array or object value that yields a series
		// ({"data": […]}, {"series": […]}, {"totals": {…}} …)
		if depth < 1 {
			keys := make([]string, 0, len(t))
			for k := range t {
				keys = append(keys, k)
			}
			sort.Strings(keys)
			for _, k := range keys {
				switch v := t[k].(type) {
				case []any, map[string]any:
					if labels, vals := extractSeries(v, depth+1); len(vals) > 0 {
						return labels, vals
					}
				}
			}
		}
		return nil, nil
	}
	return nil, nil
}

// numericMap charts the numeric entries of a map, ignoring non-numeric values, so real-world
// payloads like {"bookings":3,"sessions":3,"byStatus":{…}} still chart. Two numbers minimum —
// a single stray count isn't a series.
func numericMap(m map[string]any) ([]string, []float64, bool) {
	keys := make([]string, 0, len(m))
	for k := range m {
		if _, ok := m[k].(float64); ok {
			keys = append(keys, k)
		}
	}
	if len(keys) < 2 {
		return nil, nil, false
	}
	sort.Strings(keys)
	var vals []float64
	for _, k := range keys {
		vals = append(vals, m[k].(float64))
	}
	return keys, vals, true
}

// seriesFromObjects picks one string-ish field as the label and one numeric field as the value,
// preferring well-known names, else the alphabetically-first candidate seen in the first row.
func seriesFromObjects(rows []any) ([]string, []float64) {
	first, ok := rows[0].(map[string]any)
	if !ok {
		return nil, nil
	}
	labelField := pickField(first, labelKeys, func(v any) bool { _, s := v.(string); return s })
	valueField := pickField(first, valueKeys, func(v any) bool { _, n := v.(float64); return n })
	if valueField == "" {
		return nil, nil
	}
	var labels []string
	var vals []float64
	for i, r := range rows {
		obj, ok := r.(map[string]any)
		if !ok {
			return nil, nil
		}
		f, ok := obj[valueField].(float64)
		if !ok {
			return nil, nil // ragged rows: not a series
		}
		lab := fmt.Sprintf("%d", i)
		if labelField != "" {
			if s, ok := obj[labelField].(string); ok {
				lab = s
			}
		}
		labels = append(labels, lab)
		vals = append(vals, f)
	}
	return labels, vals
}

func pickField(row map[string]any, preferred []string, match func(any) bool) string {
	for _, k := range preferred {
		if v, ok := row[k]; ok && match(v) {
			return k
		}
	}
	keys := make([]string, 0, len(row))
	for k := range row {
		keys = append(keys, k)
	}
	sort.Strings(keys)
	for _, k := range keys {
		if match(row[k]) {
			return k
		}
	}
	return ""
}

func renderBarChart(labels []string, vals []float64) string {
	n := len(vals)
	shown := n
	if shown > chartMaxRows {
		shown = chartMaxRows
	}
	maxAbs := 0.0
	for _, v := range vals[:shown] {
		if a := math.Abs(v); a > maxAbs {
			maxAbs = a
		}
	}
	lw := 0
	for _, l := range labels[:shown] {
		if len([]rune(l)) > lw {
			lw = len([]rune(l))
		}
	}
	if lw > chartLabelMax {
		lw = chartLabelMax
	}
	var b strings.Builder
	for i := 0; i < shown; i++ {
		bar := 0
		if maxAbs > 0 {
			bar = int(math.Abs(vals[i])/maxAbs*float64(chartBarWidth) + 0.5)
		}
		if bar == 0 && vals[i] != 0 {
			bar = 1 // nonzero values always get a visible bar
		}
		b.WriteString(fmt.Sprintf("%-*s %s %s\n",
			lw, trunc(labels[i], chartLabelMax), strings.Repeat("█", bar), formatChartVal(vals[i])))
	}
	if n > shown {
		b.WriteString(fmt.Sprintf("… %d more\n", n-shown))
	}
	return b.String()
}

func formatChartVal(v float64) string {
	if v == math.Trunc(v) && math.Abs(v) < 1e12 {
		return fmt.Sprintf("%d", int64(v))
	}
	return strings.TrimRight(strings.TrimRight(fmt.Sprintf("%.2f", v), "0"), ".")
}
