module "fck-nat" {
  source = "RaJiska/fck-nat/aws"
  version = "1.3.0"

  name                 = "fck-nat-${local.project_name}"
  vpc_id               = module.vpc.vpc_id
  subnet_id            = module.vpc.public_subnets[0]
  use_cloudwatch_agent = true
  use_ssh             = true
  ssh_key_name        = "WiktorPC"

  update_route_tables = true
  route_tables_ids = {
    for rt_id in module.vpc.private_route_table_ids : rt_id => rt_id
  }
}