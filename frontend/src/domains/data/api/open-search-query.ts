import { queryOptions } from '@tanstack/react-query'

import type {
  GeneratedOpenSearchChart,
  OpenSearchField,
  OpenSearchRequest,
} from '../model/open-search'
import { fetchOpenSearchPage, searchOpenSearch } from './open-search-api'

export const openSearchPageQueryOptions = (projectId: string, index: string) =>
  queryOptions({
    queryKey: ['open-search-page', projectId, index],
    queryFn: ({ signal }) => fetchOpenSearchPage(projectId, index, signal),
  })

export const openSearchResultQueryOptions = (
  projectId: string,
  request: OpenSearchRequest | null,
) =>
  queryOptions({
    queryKey: ['open-search-result', projectId, request],
    queryFn: ({ signal }) => {
      if (request === null) throw new Error('A search request is required.')
      return searchOpenSearch(projectId, request, signal)
    },
    enabled: request !== null,
  })

function usefulCategory(field: OpenSearchField): boolean {
  if (!field.aggregatable || !['keyword', 'boolean'].includes(field.type)) return false
  return !/id|hash|url|path|geometry|coordinate|description|address|title|raw/i.test(field.name)
}

function numeric(field: OpenSearchField): boolean {
  return field.aggregatable && /byte|short|integer|long|float|double|scaled_float/.test(field.type)
}

export const generatedOpenSearchChartsQueryOptions = (
  projectId: string,
  indexPattern: string,
  queryText: string,
  fields: OpenSearchField[],
) =>
  queryOptions({
    queryKey: ['open-search-generated-charts', projectId, indexPattern, queryText, fields],
    queryFn: async ({ signal }): Promise<GeneratedOpenSearchChart[]> => {
      const date = fields.find((field) => field.aggregatable && field.type.startsWith('date'))
      const categories = fields.filter(usefulCategory).slice(0, 2)
      const number = fields.find(numeric)
      const candidates: {
        id: string
        title: string
        subtitle: string
        bucketField: string
        bucketType: string
        chartType: string
        metricType: string
        metricField: string | null
        dateInterval: string | null
      }[] = []
      if (date !== undefined)
        candidates.push({
          id: `date:${date.name}`,
          title: `${date.name} over time`,
          subtitle: `Monthly document count by ${date.name}`,
          bucketField: date.name,
          bucketType: 'date_histogram',
          chartType: 'line',
          metricType: 'count',
          metricField: null,
          dateInterval: 'month',
        })
      for (const category of categories)
        candidates.push({
          id: `terms:${category.name}`,
          title: `Top ${category.name} values`,
          subtitle: `Document count by ${category.name}`,
          bucketField: category.name,
          bucketType: 'terms',
          chartType: 'bar',
          metricType: 'count',
          metricField: null,
          dateInterval: null,
        })
      if (number !== undefined && categories[0] !== undefined)
        candidates.push({
          id: `avg:${number.name}:${categories[0].name}`,
          title: `Average ${number.name} by ${categories[0].name}`,
          subtitle: `${number.name} grouped by ${categories[0].name}`,
          bucketField: categories[0].name,
          bucketType: 'terms',
          chartType: 'bar',
          metricType: 'avg',
          metricField: number.name,
          dateInterval: null,
        })

      const results = await Promise.all(
        candidates.slice(0, 4).map(async (candidate) => {
          try {
            const result = await searchOpenSearch(
              projectId,
              {
                indexPattern,
                queryText,
                page: 1,
                pageSize: 1,
                bucketField: candidate.bucketField,
                bucketType: candidate.bucketType,
                chartType: candidate.chartType,
                metricType: candidate.metricType,
                metricField: candidate.metricField,
                dateInterval: candidate.dateInterval,
              },
              signal,
            )
            return result.chartSpecJson === null
              ? null
              : { ...candidate, chartSpecJson: result.chartSpecJson }
          } catch {
            return null
          }
        }),
      )
      return results.flatMap((chart): GeneratedOpenSearchChart[] =>
        chart === null
          ? []
          : [
              {
                id: chart.id,
                title: chart.title,
                subtitle: chart.subtitle,
                chartSpecJson: chart.chartSpecJson,
              },
            ],
      )
    },
    enabled: indexPattern !== '' && fields.length > 0,
  })
