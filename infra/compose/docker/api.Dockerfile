FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/backend ./src/backend
WORKDIR /src/src/backend/Pwdmgr.Api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
ARG GIT_SHA=unknown
ENV PWDMGR_COMMIT=$GIT_SHA
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Pwdmgr.Api.dll"]

