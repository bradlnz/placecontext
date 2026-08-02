# Progress — Production deployment recovery

Timestamp: 2026-08-02T08:51:13Z

## Root causes

- `TriggerOpenSearchSyncHandler` existed but was omitted from the Application composition root, so the dispatcher could not resolve its closed `ICommandHandler<TriggerOpenSearchSyncCommand, OpenSearchSyncView>` service.
- The DA deployment bundle was maintained separately from the deployer's file manifest and omitted both `da-pdf` runtime files.
- The OpenSearch sync token was written as the first line of a remote `bash -s` program, causing Bash to try to execute the token as a command.

## Corrections

- Registered the OpenSearch sync command handler in `AddApplication()`.
- Added a regression test that validates the handler registration.
- Added a deployment regression test that derives every required DA file from `deploy_da_application.py` and compares it with `deploy.sh`.
- Added the DA PDF source and requirements files to the deployment bundle.
- Separated secure token input from the remote deployment script so the token is read as data rather than interpreted as shell code.

## Verification

- OpenSearch sync Application tests: 2 passed.
- DA deployment bundle regression test: 1 passed.
- `deploy.sh` passed Bash syntax validation and ShellCheck.
- PlaceContext Host build succeeded; one pre-existing nullable warning remains in `ChatViewModel.Formatting.cs`.
