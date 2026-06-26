package main

import (
	"context"
	"fmt"
	"strconv"
	"strings"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

// appendCap appends v to s, keeping at most the last n samples.
func appendCap(s []float64, v float64, n int) []float64 {
	s = append(s, v)
	if len(s) > n {
		s = s[len(s)-n:]
	}
	return s
}

// fetchMetrics sums CPU (millicores) and memory (MiB) across the namespace's pods via `kubectl top`.
func (m model) fetchMetrics() tea.Cmd {
	mc := m
	return func() tea.Msg {
		ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
		defer cancel()
		b, err := mc.kubectl(ctx, "-n", ns, "top", "pods", "--no-headers")
		if err != nil {
			return metricsMsg{err: "metrics unavailable (is metrics-server up?): " + err.Error()}
		}
		var cpu, mem float64
		for _, ln := range strings.Split(strings.TrimSpace(string(b)), "\n") {
			f := strings.Fields(ln)
			if len(f) < 3 {
				continue
			}
			cpu += parseCPU(f[1])
			mem += parseMem(f[2])
		}
		return metricsMsg{cpu: cpu, mem: mem}
	}
}

// parseCPU turns a kubectl cpu quantity ("10m", "1") into millicores.
func parseCPU(s string) float64 {
	if strings.HasSuffix(s, "m") {
		v, _ := strconv.ParseFloat(strings.TrimSuffix(s, "m"), 64)
		return v
	}
	v, _ := strconv.ParseFloat(s, 64)
	return v * 1000
}

// parseMem turns a kubectl memory quantity ("126Mi", "1Gi", "500Ki") into MiB.
func parseMem(s string) float64 {
	switch {
	case strings.HasSuffix(s, "Gi"):
		v, _ := strconv.ParseFloat(strings.TrimSuffix(s, "Gi"), 64)
		return v * 1024
	case strings.HasSuffix(s, "Mi"):
		v, _ := strconv.ParseFloat(strings.TrimSuffix(s, "Mi"), 64)
		return v
	case strings.HasSuffix(s, "Ki"):
		v, _ := strconv.ParseFloat(strings.TrimSuffix(s, "Ki"), 64)
		return v / 1024
	default:
		v, _ := strconv.ParseFloat(s, 64)
		return v / (1024 * 1024)
	}
}

// lineChart renders a time series as a filled area graph (w×h cells), coloured by `color`. The area
// fill (rather than a thin line) keeps it clearly visible even when the series is flat or sparse.
func lineChart(series []float64, w, h, color int) string {
	cv := newCanvas(w, h)
	// dotted baseline + left axis so an empty/low chart still reads as a chart
	for x := 0; x < w; x++ {
		cv.set(x, h-1, '·', 5)
	}
	for y := 0; y < h; y++ {
		cv.set(0, y, '·', 5)
	}
	if len(series) == 0 {
		cv.text(2, h/2, "collecting…", 5)
		return cv.String()
	}
	s := series
	if len(s) > w-1 {
		s = s[len(s)-(w-1):]
	}
	maxv := 0.0
	for _, v := range s {
		if v > maxv {
			maxv = v
		}
	}
	maxv *= 1.15 // headroom so a flat line isn't pinned to the top edge
	if maxv <= 0 {
		maxv = 1
	}
	// columns: area filled from the value down to the baseline; the topmost cell is a bright cap
	for i, v := range s {
		x := w - len(s) + i
		top := int(float64(h-2) - (v/maxv)*float64(h-2))
		if top < 0 {
			top = 0
		} else if top > h-2 {
			top = h - 2
		}
		for y := top + 1; y < h-1; y++ {
			cv.set(x, y, '│', color)
		}
		cv.set(x, top, '▄', color)
	}
	return cv.String()
}

func lastOf(s []float64) float64 {
	if len(s) == 0 {
		return 0
	}
	return s[len(s)-1]
}
func maxOf(s []float64) float64 {
	m := 0.0
	for _, v := range s {
		if v > m {
			m = v
		}
	}
	return m
}

func (m model) metricsView() string {
	var b strings.Builder
	b.WriteString(titleStyle.Render(" metrics ") + dimStyle.Render("  live CPU + memory across the workload (kubectl top)") + "\n")
	if !m.state.reach {
		return b.String() + "  " + warnStyle.Render("● no cluster") + "\n"
	}
	if m.metricsErr != "" {
		return b.String() + "  " + errStyle.Render(m.metricsErr) + "\n"
	}
	w := m.w - 4
	if w < 20 {
		w = 76
	}
	chartH := (m.h - 8) / 2
	if chartH < 3 {
		chartH = 3
	} else if chartH > 12 {
		chartH = 12
	}
	b.WriteString("  " + scenePalette[2].Render(fmt.Sprintf("CPU  cur %.0fm  peak %.0fm", lastOf(m.cpuHist), maxOf(m.cpuHist))) + "\n")
	b.WriteString(lineChart(m.cpuHist, w, chartH, 2))
	b.WriteString("  " + scenePalette[1].Render(fmt.Sprintf("Memory  cur %.0fMi  peak %.0fMi", lastOf(m.memHist), maxOf(m.memHist))) + "\n")
	b.WriteString(lineChart(m.memHist, w, chartH, 1))
	return b.String()
}
