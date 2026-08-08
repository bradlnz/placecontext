import { deleteRequest, getJson, postJson, putJson } from '../../../shared/api/http-client'
import type { SchedulePageModel, ScheduleTrigger } from '../model/schedules'
import { schedulePage, trigger } from './schedules-schemas'
const path = (projectId: string) =>
  `/api/v1/projects/${encodeURIComponent(projectId)}/schedule-page`
export const fetchSchedules = (
  projectId: string,
  signal: AbortSignal,
): Promise<SchedulePageModel> => getJson({ path: path(projectId), schema: schedulePage, signal })
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
