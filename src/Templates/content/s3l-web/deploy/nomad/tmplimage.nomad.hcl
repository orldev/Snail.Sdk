# The application as a Nomad job, behind traefik, from the image a version tag pushed:
#
#   nomad job run deploy/nomad/tmplimage.nomad.hcl
#
# Proxy__Networks names the network traefik connects from, so the application believes its X-Forwarded headers and
# nobody else's. Secrets come from Nomad variables, never from this file.

job "tmplimage" {
  type = "service"

  group "app" {
    network {
      port "http" {
        to = 8080
      }
    }

    service {
      name = "tmplimage"
      port = "http"
      tags = [
        "traefik.enable=true",
        "traefik.http.routers.tmplimage.rule=Host(`tmplimage.example.com`)",
      ]

      check {
        type     = "http"
        path     = "/health"
        interval = "10s"
        timeout  = "2s"
      }
    }

    task "app" {
      driver = "docker"

      config {
        image = "ghcr.io/orldev/tmplimage:latest"
        ports = ["http"]
      }

      env {
        Proxy__Networks__0 = "172.16.0.0/12"
      }
#if (UsesJwt || mongo || rabbit)

      template {
        destination = "secrets/app.env"
        env         = true
        data        = <<-EOT
#if (UsesJwt)
          {{ with nomadVar "nomad/jobs/tmplimage" }}Jwt__SecretKey={{ .JwtSecretKey }}{{ end }}
#endif
#if (mongo)
          {{ with nomadVar "nomad/jobs/tmplimage" }}Mongodb__Connection={{ .MongodbConnection }}{{ end }}
#endif
#if (rabbit)
          {{ with nomadVar "nomad/jobs/tmplimage" }}RabbitMQ__HostName={{ .RabbitHost }}{{ end }}
#endif
        EOT
      }
#endif

      resources {
        cpu    = 200
        memory = 256
      }
    }
  }
}
