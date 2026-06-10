// Parametry środowiska PROD — demo / zaliczenie. Te same SKU co dev
// (subskrypcja studencka), różni się tylko nazewnictwem i izolacją RG.
using 'main.bicep'

param environment = 'prod'
param location = 'westeurope'
param namePrefix = 'hg'
