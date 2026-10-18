using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenIddict.Abstractions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Scopes;
using Volo.Abp.Uow;

namespace Grid.OpenIddict;

/* Creates initial data that is needed to property run the application
 * and make client-to-server communication possible.
 */
public class OpenIddictDataSeedContributor : OpenIddictDataSeedContributorBase, IDataSeedContributor, ITransientDependency
{
    public OpenIddictDataSeedContributor(
        IConfiguration configuration,
        IOpenIddictApplicationRepository openIddictApplicationRepository,
        IAbpApplicationManager applicationManager,
        IOpenIddictScopeRepository openIddictScopeRepository,
        IOpenIddictScopeManager scopeManager)
        : base(configuration, openIddictApplicationRepository, applicationManager, openIddictScopeRepository, scopeManager)
    {
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await CreateScopesAsync();
        await CreateApplicationsAsync();
    }

    private async Task CreateScopesAsync()
    {
        await CreateScopesAsync(new OpenIddictScopeDescriptor 
        {
            Name = "Grid", 
            DisplayName = "Grid API", 
            Resources = { "Grid" }
        });
    }

    private async Task CreateApplicationsAsync()
    {
        var commonScopes = new List<string> {
            OpenIddictConstants.Permissions.Scopes.Address,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
            "Grid"
        };

        var configurationSection = Configuration.GetSection("OpenIddict:Applications");


        // Console Test / Angular Client
        
        var appClientId = configurationSection["Grid_App:ClientId"] ?? "Grid_App";
        var appClientRootUrl = (configurationSection["Grid_App:RootUrl"] ?? "http://localhost:5173").TrimEnd('/');
        await CreateOrUpdateApplicationAsync(
            applicationType: OpenIddictConstants.ApplicationTypes.Web,
            name: appClientId,
            type: OpenIddictConstants.ClientTypes.Public,
            consentType: OpenIddictConstants.ConsentTypes.Implicit,
            displayName: "Console Test / Angular Application",
            secret: null,
            grantTypes: new List<string> {
                OpenIddictConstants.GrantTypes.AuthorizationCode,
                OpenIddictConstants.GrantTypes.Password,
                OpenIddictConstants.GrantTypes.ClientCredentials,
                OpenIddictConstants.GrantTypes.RefreshToken,
                "LinkLogin",
                "Impersonation"
            },
            scopes: commonScopes,
            redirectUris: new List<string> { appClientRootUrl },
            postLogoutRedirectUris: new List<string> { appClientRootUrl },
            clientUri: appClientRootUrl,
            logoUri: "/images/clients/angular.svg"
        );

        
        




        // Swagger Client
        var swaggerClientId = configurationSection["Grid_Swagger:ClientId"] ?? "Grid_Swagger";
        var swaggerRootUrl = (configurationSection["Grid_Swagger:RootUrl"] ?? "http://localhost:44344").TrimEnd('/');

        await CreateOrUpdateApplicationAsync(
            applicationType: OpenIddictConstants.ApplicationTypes.Web,
            name: swaggerClientId,
            type: OpenIddictConstants.ClientTypes.Public,
            consentType: OpenIddictConstants.ConsentTypes.Implicit,
            displayName: "Swagger Application",
            secret: null,
            grantTypes: new List<string> { OpenIddictConstants.GrantTypes.AuthorizationCode, },
            scopes: commonScopes,
            redirectUris: new List<string> { $"{swaggerRootUrl}/swagger/oauth2-redirect.html" },
            clientUri: swaggerRootUrl.EnsureEndsWith('/') + "swagger",
            logoUri: "/images/clients/swagger.svg"
        );


    }
}
