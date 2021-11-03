# Copyright 2021 Google LLC
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

variable "project_id" {
  type        = string
  description = "Project to deploy resources to"
}

variable "region" {
  type        = string
  description = "Google Cloud region to deploy resources to"
  default     = "us-central1"
}

variable "web_app_image" {
  type        = string
  description = "Image name for web app"
  default     = "dotnet-e2e-diagnostics-web-app"
}

variable "listener_app_image" {
  type        = string
  description = "Image name for listener app"
  default     = "dotnet-e2e-diagnostics-listener-app"
}

variable "cluster_name" {
  type        = string
  description = "GKE Auto-pilot cluster name"
  default     = "cluster-1"
}
