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

provider "google" {
  project = var.project_id
  region  = var.region
}

locals {
    web_app_build_command = <<EOF
        gcloud auth configure-docker --quiet --project ${var.project_id}
        docker build -t gcr.io/${var.project_id}/${var.web_app_image}:latest -f ../dockerfile-web-app ../
        docker push gcr.io/${var.project_id}/${var.web_app_image}:latest
    EOF

    workload_identity_annotate_command = <<EOF
        gcloud container clusters get-credentials ${var.cluster_name} --project ${var.project_id} --region ${var.region}
        kubectl annotate --overwrite sa -n default default iam.gke.io/gcp-service-account=${google_service_account.listener-sa.email}
    EOF

    listener_app_build_and_deploy_command = <<EOF
        export PROJECT_ID=${var.project_id}
        export IMAGE_NAME=${var.listener_app_image}

        gcloud auth configure-docker --quiet --project $PROJECT_ID
        docker build -t gcr.io/$PROJECT_ID/$IMAGE_NAME:latest -f ../dockerfile-listener-app ../
        docker push gcr.io/$PROJECT_ID/$IMAGE_NAME:latest

        gcloud container clusters get-credentials ${var.cluster_name} --project $PROJECT_ID --region ${var.region}
        envsubst < ../deployment-listener.template.yaml | kubectl apply -f -
    EOF
}

data "google_project" "project" {
  project_id = var.project_id
}

# Google Cloud services used in the tutorial
resource "google_project_service" "services" {
  project = var.project_id
  for_each = toset([
    "cloudbuild.googleapis.com",
    "compute.googleapis.com",
    "containerregistry.googleapis.com",
    "container.googleapis.com",
    "run.googleapis.com",
    "cloudfunctions.googleapis.com"
  ])
  service            = each.value
  disable_on_destroy = false
}

# Resources for PubSub topic and subscription
# PubSub topic, subscription 


# Resources for the Echo Cloud Function
# Service account for function, SA IAM, zip and upload source to a bucket, 
# cloud function, and function access permissions

resource "google_service_account" "function-sa" {
  project      = var.project_id
  account_id   = "echo-function-sa"
  display_name = "Echo Function on Cloud Functions"

  depends_on = [
    google_project_service.services["iam.googleapis.com"]
  ]
}

resource "google_project_iam_member" "function-sa-iam-roles" {
  project = var.project_id
  for_each = toset([
    "roles/cloudtrace.agent",
    "roles/logging.logWriter"])
  role    = each.key
  member  = "serviceAccount:${google_service_account.function-sa.email}"
}

data "archive_file" "source" {
  type        = "zip"
  source_dir  = "${path.root}/../cloudfunctions-echo-function"
  output_path = "/tmp/echo-function.zip"
}

resource "google_storage_bucket" "bucket" {
  name = "${var.project_id}-function"
}

# Add source code zip to bucket
resource "google_storage_bucket_object" "zip" {
  # Append file MD5 to force bucket to be recreated
  #name   = "source.zip#${data.archive_file.source.output_md5}"
  name   = "source.zip"
  bucket = google_storage_bucket.bucket.name
  source = data.archive_file.source.output_path
}

resource "google_cloudfunctions_function" "echo-function" {
  name        = "dotnet-e2e-diagnostics-echo"
  runtime     = "dotnet3"

  source_archive_bucket = google_storage_bucket.bucket.name
  source_archive_object = google_storage_bucket_object.zip.name
  trigger_http          = true
  entry_point           = "GoogleCloudSamples.EndToEndTracing.Function.EchoFunction"
  region                = var.region
  service_account_email = google_service_account.function-sa.email
  environment_variables = {
    GoogleCloud__Diagnostics__ProjectId = var.project_id
    GoogleCloud__Diagnostics__ServiceName = "Echo"
    GoogleCloud__Diagnostics__Version = "1.0"
  }

  depends_on = [
    google_project_service.services["cloudfunctions.googleapis.com"]
  ]
}

resource "google_cloudfunctions_function_iam_member" "echo-function-invoker" {
  project        = google_cloudfunctions_function.echo-function.project
  region         = google_cloudfunctions_function.echo-function.region
  cloud_function = google_cloudfunctions_function.echo-function.name

  role   = "roles/cloudfunctions.invoker"
  member = "allUsers"
}

# Resources for the web application on Cloud Run
# Service account, SA IAM, PubSub topic and IAM
# build and push a docker image, Cloud Run service,
# and Cloud Run access permissions

resource "google_service_account" "web-app-sa" {
  project      = var.project_id
  account_id   = "web-app-sa"
  display_name = "Web application on Cloud Run"

  depends_on = [
    google_project_service.services["iam.googleapis.com"]
  ]
}

resource "google_project_iam_member" "web-app-sa-iam-roles" {
  for_each = toset([
    "roles/cloudtrace.agent",
    "roles/logging.logWriter"])
  role    = each.key
  member = "serviceAccount:${google_service_account.web-app-sa.email}"
}

resource "google_pubsub_topic" "echo-topic" {
  name = "echo"
}

resource "google_pubsub_topic_iam_binding" "publisher" {
  topic = google_pubsub_topic.echo-topic.name
  role         = "roles/pubsub.publisher"
  members = ["serviceAccount:${google_service_account.web-app-sa.email}"]
}

resource "null_resource" "gcr_web_app_docker_image" {
  provisioner "local-exec" {
    command = "${local.web_app_build_command}"
  }

  depends_on = [
    google_project_service.services["containerregistry.googleapis.com"]
  ]
}

resource "google_cloud_run_service" "web-app" {
  name     = "dotnet-e2e-diagnostics-web-app"
  project  = var.project_id
  location = var.region

  autogenerate_revision_name = true

  template {
    spec {
      service_account_name = google_service_account.web-app-sa.email

      containers {
        image = "gcr.io/${var.project_id}/${var.web_app_image}:latest"
        env {
            name = "GoogleCloud__ProjectId"
            value = var.project_id
        }
         env {
            name = "GoogleCloud__TopicId"
            value = google_pubsub_topic.echo-topic.name
        }
        env {
            name = "GoogleCloud__EchoFunctionUrl"
            value = google_cloudfunctions_function.echo-function.https_trigger_url
        }
      }
    }
  }
  depends_on = [
    google_project_service.services["run.googleapis.com"],
    null_resource.gcr_web_app_docker_image
  ]
}

resource "google_cloud_run_service_iam_member" "cloud-run-noauth" {
  project     = google_cloud_run_service.web-app.project
  location    = google_cloud_run_service.web-app.location
  service     = google_cloud_run_service.web-app.name
  role = "roles/run.invoker"
  member = "allUsers"
}

# Resources for the listener app on GKE
# GKE Cluster, service account, SA IAM, 
# Workload Identity configuration, PubSub subscription and IAM,
# build and push a docker image, and deploy the listener app

resource "google_container_cluster" "gke-cluster" {
  name                     = var.cluster_name
  location                 = var.region

  # Enable Autopilot for this cluster
  enable_autopilot = true
  vertical_pod_autoscaling {
    enabled = true
  }

  depends_on = [
    google_project_service.services["container.googleapis.com"]
  ]
}

resource "google_service_account" "listener-sa" {
  project      = var.project_id
  account_id   = "pubsub-listener-sa"
  display_name = "PubSub listener on GKE"

  depends_on = [
    google_project_service.services["iam.googleapis.com"]
  ]
}

resource "google_project_iam_member" "listener-sa-iam-roles" {
  for_each = toset([
    "roles/cloudtrace.agent",
    "roles/logging.logWriter"])
  role    = each.key
  member = "serviceAccount:${google_service_account.listener-sa.email}"
}

resource "google_pubsub_subscription" "echo-subscription" {
  name  = "default"
  topic = google_pubsub_topic.echo-topic.name
}

resource "google_pubsub_subscription_iam_binding" "subscriber" {
  subscription = google_pubsub_subscription.echo-subscription.name
  role         = "roles/pubsub.subscriber"
  members = ["serviceAccount:${google_service_account.listener-sa.email}"]
}

resource "google_service_account_iam_member" "workload-identity" {
  service_account_id = google_service_account.listener-sa.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "serviceAccount:${var.project_id}.svc.id.goog[default/default]"

  depends_on = [
    google_container_cluster.gke-cluster
  ]
}

resource "null_resource" "workload-idnetity-annotate" {
  provisioner "local-exec" {
    command = "${local.workload_identity_annotate_command}"
  }

  depends_on = [
    google_container_cluster.gke-cluster,
    google_service_account_iam_member.workload-identity
  ]
}

resource "null_resource" "listener_app" {
  provisioner "local-exec" {
    command = "${local.listener_app_build_and_deploy_command}"
  }

  depends_on = [
    null_resource.workload-idnetity-annotate,
    google_project_iam_member.listener-sa-iam-roles,
    google_pubsub_subscription_iam_binding.subscriber
  ]
}

output "cloud_run_url" {
	value = google_cloud_run_service.web-app.status[0].url
}