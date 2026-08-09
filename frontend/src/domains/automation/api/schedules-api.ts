import { deleteRequest, getJson, postJson, putJson } from '../../../shared/api/http-client'
import type { SchedulePageModel, ScheduleTrigger } from '../model/schedules'
import { scheduleDataTables, schedulePage, scheduleServicePage, trigger } from './schedules-schemas'
const path = (projectId: string) =>
  `/api/jobs/projects/${encodeURIComponent(projectId)}/schedule-page`
export const fetchSchedules = (
  projectId: string,
  signal: AbortSignal,
): Promise<SchedulePageModel> =>
  Promise.all([
    getJson({ path: path(projectId), schema: scheduleServicePage, signal }),
    getJson({
      path: `/api/data/projects/${encodeURIComponent(projectId)}/tables`,
      schema: scheduleDataTables,
      signal,
    }),
  ]).then(([page, tables]) =>
    schedulePage.parse({ ...page, tables: tables.map((table) => table.name) }),
  )
export const createSchedule = (
  projectId: string,
  body: object,
  signal: AbortSignal,
): Promise<ScheduleTrigger> =>
  postJson({
    path: `${path(projectId)}/triggers`,
    body,
    schema: trigger,
    signal,
  })
export const updateSchedule = (
  projectId: string,
  id: string,
  body: object,
  signal: AbortSignal,
): Promise<ScheduleTrigger> =>
  putJson({
    path: `${path(projectId)}/triggers/${id}`,
    body,
    schema: trigger,
    signal,
  })
export const deleteSchedule = (projectId: string, id: string, signal: AbortSignal): Promise<void> =>
  deleteRequest(`${path(projectId)}/triggers/${id}`, signal)
