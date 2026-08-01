# Events and schedules

*Start Jobs automatically when something happens or at a chosen time.*

## The difference in plain language

An **event** records that something happened, such as `customer.created` or `invoice.paid`.
An **event trigger** listens for one event name and starts a Job when it appears.
A **schedule** starts a Job at a particular time or interval.

Events do not run Jobs by themselves. At least one enabled event trigger must be listening for the
event name.

## Use the Events screen

Open **Events** to see a summary, the available event types, and the latest activity.

- **Built-in** event types come from PlaceContext and cannot be redefined.
- **Custom** event types describe activities used by your workspace.
- **Active triggers** shows how many enabled Job triggers are listening.
- **Recent activity** shows whether an event came from the system or was emitted manually. Open
  **View payload** when you need to inspect the data sent with it.

Select **New event type** to add a custom name, description, and optional payload guidance. A clear
name such as `application.approved` is easier for everyone to recognise than a vague name such as
`done`.

## Test an event manually

Select **Emit** beside an event type. You can include an optional JSON payload. The confirmation
panel tells you how many active triggers are listening before you continue.

After emission, the activity appears at the top of **Recent activity**. The message above the page
also reports how many triggers fired.

> Emitting an event can start real Jobs. Check the active trigger count and payload before you
> continue, especially in a production workspace.

## Create and manage triggers

Open **Schedules** to manage every trigger in the current project. Select:

- **Schedule** to run a Job hourly, daily, on weekdays, weekly, monthly, or with an advanced cron
  expression;
- **Event** to choose an event type and the Job that should run when it is emitted;
- **Launchpad** to run a chain as an autonomous agent session on a schedule, optionally using a
  project table as source data.

Simple schedules use the workspace timezone shown on the screen. The table shows whether each
trigger is on or paused, its target, its next run, and when it last fired. Use **Pause** when you
want to keep a trigger without allowing it to start work, and **Resume** when it is safe to run
again.

You can also open a Job, select its **Triggers** tab, and manage triggers for that Job only.

## If an event did not start a Job

Check these items in order:

1. Confirm the event appears under **Events → Recent activity**.
2. Confirm the trigger is **ON**, not paused.
3. Confirm the event name on the trigger exactly matches the emitted event type.
4. Confirm the trigger points to the intended Job.
5. Open the Job's **Runs** tab or **Observability** to see whether the run started and failed.

Creating, editing, pausing, or emitting events and triggers requires the matching workspace
permission. Ask an administrator if the controls are missing.
