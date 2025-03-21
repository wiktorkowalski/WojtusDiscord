resource "aws_route53_record" "nginx_record" {
  zone_id = data.aws_route53_zone.wiktorkowalski.zone_id
  name    = "nginx.wiktorkowalski.pl"
  type    = "A"

  alias {
    name                   = aws_lb.nginx_alb.dns_name
    zone_id                = aws_lb.nginx_alb.zone_id
    evaluate_target_health = true
  }
}

data "aws_route53_zone" "wiktorkowalski" {
  name         = "wiktorkowalski.pl"
  private_zone = false
}
