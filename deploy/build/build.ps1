# Build de l'image mono-conteneur + push + déploiement Helm.
# Usage : ./deploy/build/build.ps1 [-Image ...] [-ChartPath ...] [-SkipDeploy]
param(
    [string]$Image = "registry.elylan/elyspio/proxmox-ip-monitor",
    # Chemin du chart, paramétré plutôt qu'en dur : le chart vit dans le dépôt
    # infrastructure-elylan, dont l'emplacement dépend de la machine.
    [string]$ChartPath = "../infrastructure-elylan/kubernetes/apps/proxmox-ip-monitor",
    [string]$Namespace = "apps",
    [switch]$SkipDeploy
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$tag = Get-Date -Format "yyyy.MM.dd.HHmm"

Write-Host "Build $Image`:$tag depuis $root" -ForegroundColor Cyan
docker build -f "$root/deploy/build/dockerfile" --build-arg APP_VERSION=$tag -t "$Image`:$tag" $root

docker push "$Image`:$tag"

if ($SkipDeploy) {
    Write-Host "Image poussée : $Image`:$tag (déploiement ignoré)" -ForegroundColor Green
    return
}

$chart = (Resolve-Path (Join-Path $root $ChartPath) -ErrorAction SilentlyContinue)
if (-not $chart) {
    throw "Chart introuvable : $ChartPath. Passez -ChartPath, ou -SkipDeploy pour ne faire que pousser l'image."
}

$values = Join-Path $chart "values.yaml"
$secrets = Join-Path $chart "values.secrets.yaml"
if (-not (Test-Path $secrets)) {
    throw "Fichier de secrets absent : $secrets. Il porte DataProtection:MasterKey et la chaîne MongoDB."
}

helm upgrade --install proxmox-ip-monitor $chart `
    -f $values -f $secrets `
    --set image.tag=$tag -n $Namespace

Write-Host "OK : $Image`:$tag" -ForegroundColor Green
