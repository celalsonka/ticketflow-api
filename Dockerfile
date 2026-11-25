# syntax=docker/dockerfile:1

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, in its own layer, so it is cached until a project file changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/TicketFlow.Domain/TicketFlow.Domain.csproj src/TicketFlow.Domain/
COPY src/TicketFlow.Application/TicketFlow.Application.csproj src/TicketFlow.Application/
COPY src/TicketFlow.Infrastructure/TicketFlow.Infrastructure.csproj src/TicketFlow.Infrastructure/
COPY src/TicketFlow.Api/TicketFlow.Api.csproj src/TicketFlow.Api/
RUN dotnet restore src/TicketFlow.Api/TicketFlow.Api.csproj

COPY src/ src/
RUN dotnet publish src/TicketFlow.Api/TicketFlow.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .

# The official image ships a non-root user; never run the app as root.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TicketFlow.Api.dll"]
