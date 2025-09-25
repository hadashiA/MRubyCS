MRuby::CrossBuild.new("windows") do |conf|
  conf.toolchain

  conf.gem core: 'mruby-compiler'
  conf.gem core: 'mruby-string-ext'  
  conf.gem './mrbgems/mrbcs-compiler'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM)
end
