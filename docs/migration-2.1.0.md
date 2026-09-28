# Migration 2.1.0

2.1.0 is the first release of the Foundation packages on nuget.org. Against 2.0.0 it adds a
`net10.0` asset to every package and moves the authentication test doubles out of `Bsft.Testing`;
only the second needs a consumer action, and only in test projects that used them.

## Added: every package targets net10.0 as well

Every package ships a `net10.0` asset beside the `net8.0` one. Nothing to do: a `net8.0`
application keeps the `net8.0` asset and the dependencies it had (Microsoft.Extensions.* 9.0.19,
ASP.NET Core 8.0.13); a `net10.0` application gets the `net10.0` asset, which depends on the 10.0.x
line of both.

One behavior an application may notice on `net10.0` is the runtime's, not a package's: since
Microsoft.Extensions.Hosting 10, a `BackgroundService` runs the whole of its `ExecuteAsync` on the
thread pool, so a Foundation worker (`OutboxDispatcher`, `JobDispatcher`, the sweepers and seeders)
stopped right after it started may not have run a pass. A test that starts and stops one waits for
the effect it asserts instead of relying on the synchronous start of earlier versions.

## Changed: the authentication test doubles left `Bsft.Testing`

`Bsft.Testing` no longer references an authentication package. The test user, the permissions of
the test principal and the `BsftTest` authentication scheme are not part of the Foundation any
more. What stays in `Bsft.Testing` is unchanged: the tenant and correlation doubles, the clock, the
mediator and saga harnesses, `AddBsftTestDatabase`, the fake OIDC authority, the fake push service
and `ITestEnvironmentPolicy`.

| Change | Consumer action |
| --- | --- |
| `TestUserContext`, `TestPermissionSource`, `ITestPermissionSource` removed from `Bsft.Testing.Contexts` | Register the test user your application's authentication expects |
| `TestPrincipalProvider`, `ITestPrincipalProvider`, `TestAuthenticationHandler` removed from `Bsft.Testing.Web` | Register a test authentication scheme of your own |
| `AddBsftTestUser`, `AddBsftTestPermissions`, `AddBsftTestAuthentication` removed from `TestingServiceCollectionExtensions` | As above |
| `AddBsftTestContexts` registers tenant and correlation only, no longer a user | Where a test relied on the signed-in user it provided, register the user itself |
| `BsftWebApplicationFactory<TEntryPoint>` installs tenant and correlation only; `CreateUserContext` removed from it | Sign the user in from your own factory, in an override of `ConfigureTestDoubles` |
| `BsftWebApplicationFactory<TEntryPoint>.ConfigureTestDoubles(IServiceCollection)` added (protected virtual) | None; a factory bringing doubles of its own overrides it and calls the base first |
| `Bsft.Testing` references `Microsoft.IdentityModel.JsonWebTokens` directly (8.12.0) | None |

No wire or persisted name changes.
