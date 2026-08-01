# CRM and customer communications

*Keep customer work, messages, files, and follow-up workflows together.*

## Client directory

Select a project and open **CRM**. The client directory can be searched by name, company, email, or
phone. Select a lifecycle stage to narrow the list:

| Stage | Everyday meaning |
|---|---|
| **Lead** | A new contact who may become a customer |
| **Qualified** | A suitable opportunity worth progressing |
| **Onboarding** | A customer currently being set up |
| **Active** | A current customer |
| **At risk** | A customer who may need attention |
| **Churned** | A former or lost customer |

Use **Add client** to record contact details and notes. Open a client to edit their information,
move their stage, or review activity.

## Notes and messages

The **Comms** tab keeps an easy-to-read timeline for that client:

- **Note** records an internal note and does not contact the client.
- **Email** sends to the email address on the client record.
- **SMS** sends to the phone number on the client record.

Email and SMS appear only when the workspace has that channel configured and you have permission to
send it. PlaceContext selects the workspace connection automatically, so day-to-day users do not
need to know or choose the delivery provider.

The timeline shows whether a message was sent or failed. Correct the client address or phone number,
or ask an administrator to check **Settings → Communications**, before trying again.

## Client files

Use the **Artifacts** tab inside a client to upload documents or find files produced by that
client's automations. Search by file name or filter by uploaded and automation-produced files.

These are customer-linked files, separate from public artifact sharing on the main **Artifacts**
page. Removing a directly uploaded client file removes its stored object as well.

## Customer automations

An automation starts a job chain when a customer event occurs. Available events include:

- a client is created or updated;
- a client enters a lifecycle stage;
- a note is added;
- a file is attached;
- a communication is sent.

Open **CRM → Automations**, choose an event and chain, optionally limit it to one lifecycle stage,
and enable it. The chain receives the client's current contact details, stage, notes, and related
context. Use the client's Overview tab to run an automation manually or review its history.

## Leads from a website form

An administrator can open **CRM → Settings** to connect one website contact form. The setup creates
a limited access token and checks the exact approved website address on every request.

The full token is shown only when generated. Copy it into the website's secure configuration, not
into public page code. **Rotate token** replaces a lost or exposed token; **Disable endpoint** stops
new form submissions. This token can create or update Lead records only. It cannot read the CRM or
use other workspace features.

## How customer information is protected

Sensitive CRM database fields are encrypted while stored, including identity and contact details,
notes, message contents, delivery details, client-file metadata, and client-linked workflow output.
Older unencrypted CRM records are upgraded automatically when the application starts.

Encryption does not replace access control: signed-in users with CRM permission can still read the
information they are allowed to use. Client file contents remain in the configured object store and
are delivered through access-controlled routes.
