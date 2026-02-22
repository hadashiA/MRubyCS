MRuby::CrossBuild.new("windows") do |conf|
  conf.toolchain

  conf.gem github: 'hadashiA/mruby-compiler2'
  conf.gem './mrbgems/mrubycs-compiler'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM MRC_TARGET_MRUBY  MRC_ALLOC_LIBC)
end
