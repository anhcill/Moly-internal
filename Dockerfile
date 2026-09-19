# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project metadata first so dependency restore stays cacheable.
COPY Directory.Build.props ./
COPY src/InternalManagement.Api/InternalManagement.Api.csproj src/InternalManagement.Api/
COPY src/InternalManagement.Application/InternalManagement.Application.csproj src/InternalManagement.Application/
COPY src/InternalManagement.Domain/InternalManagement.Domain.csproj src/InternalManagement.Domain/
COPY src/InternalManagement.Infrastructure/InternalManagement.Infrastructure.csproj src/InternalManagement.Infrastructure/

RUN dotnet restore src/InternalManagement.Api/InternalManagement.Api.csproj

COPY src ./src
RUN dotnet publish src/InternalManagement.Api/InternalManagement.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

COPY --from=build /app/publish ./

# The official ASP.NET image provides APP_UID for non-root execution.
USER $APP_UID

ENTRYPOINT ["dotnet", "InternalManagement.Api.dll"]
