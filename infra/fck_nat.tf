module "fck-nat" {
  source = "RaJiska/fck-nat/aws"
  version = "1.3.0"

  name                 = "fck-nat-${local.project_name}"
  vpc_id               = module.vpc.vpc_id
  subnet_id            = module.vpc.public_subnets[0]
  use_cloudwatch_agent = true
  use_ssh             = true
  ssh_key_name        = "WiktorPC"

  update_route_tables = false

  depends_on = [ module.vpc ]
}

# manually created route table for nat gateway
resource "aws_route" "nat_gateway" {
  count = length(module.vpc.private_route_table_ids)
  
  route_table_id         = module.vpc.private_route_table_ids[count.index]
  destination_cidr_block = "0.0.0.0/0"
  network_interface_id   = module.fck-nat.eni_id
  
  depends_on = [module.fck-nat, module.vpc]
}